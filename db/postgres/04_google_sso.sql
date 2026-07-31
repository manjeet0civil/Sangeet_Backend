-- =====================================================================
--  04_google_sso.sql — sign in with Google
--
--  Safe to run more than once (every statement is guarded), and safe to run
--  on a live database: it only adds nullable columns and new functions.
--  Existing email/password accounts are untouched and keep working.
--
--  Run AFTER 01_schema.sql, 02_functions.sql and 03_artist_category_lyrics.sql.
-- =====================================================================

-- ---------------------------------------------------------------- schema
--
-- An account can now be reached two ways, and either half may be missing:
--   passwordhash IS NOT NULL  -> can sign in with a password
--   googlesubject IS NOT NULL -> can sign in with Google
-- An account that did both has both. One that has neither cannot sign in at all,
-- which is why nothing ever clears both.
ALTER TABLE account ALTER COLUMN passwordhash DROP NOT NULL;

-- Google's 'sub' claim: the stable, unique id for a Google account. Deliberately NOT the email —
-- Google lets people change the address on an account, and the sub survives that.
ALTER TABLE account ADD COLUMN IF NOT EXISTS googlesubject text NULL;

-- Partial unique index: one Google identity may back at most one Sangeet account, while the
-- many rows with NULL (ordinary password accounts) are all allowed to coexist.
CREATE UNIQUE INDEX IF NOT EXISTS ux_account_googlesubject
    ON account (googlesubject) WHERE googlesubject IS NOT NULL;

-- ------------------------------------------------------------- functions
--
-- Same conventions as 02_functions.sql: named parameters (the repositories call
-- `SELECT * FROM fn(p_x => @X)`), and business errors raised as custom SQLSTATEs
-- that PostgresErrorTranslator maps to HTTP statuses.

-- procAccountGetByGoogle — look up an account by its Google subject.
-- Mirrors procAccountLogin, including the isactive filter: a deactivated account
-- is simply not found, so it cannot sign in by any route.
CREATE OR REPLACE FUNCTION procaccountgetbygoogle(p_googlesubject text)
RETURNS TABLE (accountid uuid, email citext, passwordhash text, isactive boolean, role varchar)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT a.accountid, a.email, a.passwordhash, a.isactive, a.role
    FROM account a
    WHERE a.googlesubject = p_googlesubject AND a.isactive = true;
END;
$$;

-- procAccountInsertGoogle — create an account that has no password at all.
-- Same shape as procAccountInsert, minus the hash.
CREATE OR REPLACE FUNCTION procaccountinsertgoogle(
    p_accountid     uuid,
    p_email         varchar,
    p_googlesubject text,
    p_role          varchar DEFAULT 'User'
)
RETURNS TABLE (
    accountid uuid, email citext, passwordhash text, isactive boolean,
    created timestamp, updated timestamp, role varchar
)
LANGUAGE plpgsql AS $$
DECLARE
    v_role varchar(20);
BEGIN
    IF EXISTS (SELECT 1 FROM account a WHERE a.email = p_email::citext) THEN
        RAISE EXCEPTION 'Email already exists.' USING ERRCODE = '50001';
    END IF;

    IF EXISTS (SELECT 1 FROM account a WHERE a.googlesubject = p_googlesubject) THEN
        RAISE EXCEPTION 'This Google account is already linked to another account.' USING ERRCODE = '50004';
    END IF;

    -- The app never grants SuperAdmin; force anything unexpected down to User.
    v_role := CASE
                WHEN lower(p_role) = 'admin' THEN 'Admin'
                WHEN lower(p_role) = 'user'  THEN 'User'
                ELSE 'User'
              END;

    INSERT INTO account (accountid, email, passwordhash, isactive, created, role, googlesubject)
    VALUES (p_accountid, p_email::citext, NULL, true, timezone('utc', now()), v_role, p_googlesubject);

    RETURN QUERY
    SELECT a.accountid, a.email, a.passwordhash, a.isactive, a.created, a.updated, a.role
    FROM account a WHERE a.accountid = p_accountid;
END;
$$;

-- procAccountLinkGoogle — attach a Google identity to an existing password account,
-- so someone who registered with a password can then use the Google button.
CREATE OR REPLACE FUNCTION procaccountlinkgoogle(
    p_accountid     uuid,
    p_googlesubject text
)
RETURNS TABLE (
    accountid uuid, email citext, passwordhash text, isactive boolean,
    created timestamp, updated timestamp, role varchar
)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM account a WHERE a.accountid = p_accountid) THEN
        RAISE EXCEPTION 'Account not found.' USING ERRCODE = '50005';
    END IF;

    -- Guard the race the unique index would otherwise turn into a raw 500.
    IF EXISTS (SELECT 1 FROM account a
               WHERE a.googlesubject = p_googlesubject AND a.accountid <> p_accountid) THEN
        RAISE EXCEPTION 'This Google account is already linked to another account.' USING ERRCODE = '50004';
    END IF;

    UPDATE account a
    SET googlesubject = p_googlesubject,
        updated       = timezone('utc', now())
    WHERE a.accountid = p_accountid;

    RETURN QUERY
    SELECT a.accountid, a.email, a.passwordhash, a.isactive, a.created, a.updated, a.role
    FROM account a WHERE a.accountid = p_accountid;
END;
$$;
