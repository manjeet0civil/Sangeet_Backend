namespace MusicWebsite.Domain.Entities;

/// <summary>
/// A song category (Bollywood, Ghazal, Devotional...). Created on demand: the first upload that
/// declares a category name creates it, everything after reuses it. Names are case-insensitive,
/// so "bollywood" and "Bollywood" are one category rather than two.
/// </summary>
public class Category
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime Created { get; set; }
    /// <summary>How many live songs use this category. Only populated by GetAll.</summary>
    public int TotalSongs { get; set; }
}
