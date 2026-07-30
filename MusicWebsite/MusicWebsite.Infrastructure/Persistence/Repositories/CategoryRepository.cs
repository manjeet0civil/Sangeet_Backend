using MusicWebsite.Application.Interfaces.Persistence;
using MusicWebsite.Domain.Entities;

namespace MusicWebsite.Infrastructure.Persistence.Repositories;

public class CategoryRepository : RepositoryBase, ICategoryRepository
{
    public CategoryRepository(IDbConnectionFactory factory) : base(factory) { }

    public Task<Category> GetOrCreateAsync(string name)
        => QueryFirstAsync<Category>(StoredProcedures.CategoryGetOrCreate, new { Name = name });

    public Task<IEnumerable<Category>> GetAllAsync()
        => QueryAsync<Category>(StoredProcedures.CategoryGetAll, new { });
}
