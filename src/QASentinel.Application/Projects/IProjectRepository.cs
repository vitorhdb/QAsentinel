using QASentinel.Domain.Entities;

namespace QASentinel.Application.Projects;

public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken);

    Task AddAsync(Project project, CancellationToken cancellationToken);
}
