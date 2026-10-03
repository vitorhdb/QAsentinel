using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QASentinel.Application.Projects;
using QASentinel.Domain.Entities;

namespace QASentinel.Desktop.ViewModels;

public sealed partial class ProjectsViewModel(IProjectRepository repository) : ObservableObject
{
    public ObservableCollection<Project> Projects { get; } = new();

    [ObservableProperty]
    private string _newProjectName = string.Empty;

    [ObservableProperty]
    private string _newProjectDescription = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Os projetos são salvos no SQLite local.";

    [RelayCommand]
    private async Task LoadAsync()
    {
        var projects = await repository.GetAllAsync(CancellationToken.None);
        Projects.Clear();
        foreach (var project in projects)
        {
            Projects.Add(project);
        }
    }

    [RelayCommand]
    private async Task CreateProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProjectName))
        {
            StatusMessage = "Informe um nome para o projeto.";
            return;
        }

        var project = new Project(NewProjectName, NewProjectDescription);
        await repository.AddAsync(project, CancellationToken.None);
        Projects.Add(project);
        NewProjectName = string.Empty;
        NewProjectDescription = string.Empty;
        StatusMessage = "Projeto criado.";
    }
}
