using MusicWebsite.Domain.Entities;

namespace MusicWebsite.Application.Interfaces.Persistence;

public interface ICategoryRepository
{
    /// <summary>
    /// Returns the category with this name, creating it if it doesn't exist yet. A blank name
    /// resolves to "Uncategorized". Safe against two uploads racing for the same new name.
    /// </summary>
    Task<Category> GetOrCreateAsync(string name);

    /// <summary>Every category with its live song count, alphabetically.</summary>
    Task<IEnumerable<Category>> GetAllAsync();
}
