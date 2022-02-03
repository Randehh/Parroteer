using Parroteer.DataSources;
using Parroteer.Projects;
using Parroteer.Utilities;
using Parroteer.Views;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace Parroteer.ViewModels {
	public class MainWindowVM : ViewModelBase {

		private SimpleCommand m_NewProjectCommand;
		public SimpleCommand NewProjectCommand {
			get => m_NewProjectCommand;
			set => SetProperty(ref m_NewProjectCommand, value);
		}

		private SimpleCommand m_OpenProjectCommand;
		public SimpleCommand OpenProjectCommand {
			get => m_OpenProjectCommand;
			set => SetProperty(ref m_OpenProjectCommand, value);
		}

		private SimpleCommand m_SaveProjectCommand;
		public SimpleCommand SaveProjectCommand {
			get => m_SaveProjectCommand;
			set => SetProperty(ref m_SaveProjectCommand, value);
		}

		private ParroteerProject m_LoadedProject;
		public ParroteerProject LoadedProject {
			get => m_LoadedProject;
			set {
				SetProperty(ref m_LoadedProject, value);
				ProjectPageViewModel.Project = value;
				OnPropertyChanged(nameof(ShowLandingPage));
				OnPropertyChanged(nameof(ShowProjectPage));
				OnPropertyChanged(nameof(ProjectPageViewModel));
			}
		}

		private ProjectPageVM m_ProjectPageViewModel = new ProjectPageVM();
		public ProjectPageVM ProjectPageViewModel => m_ProjectPageViewModel;
		public Visibility ShowLandingPage => LoadedProject == null ? Visibility.Visible : Visibility.Collapsed;
		public Visibility ShowProjectPage => LoadedProject != null ? Visibility.Visible : Visibility.Collapsed;

		public MainWindowVM() {
			NewProjectCommand = new SimpleCommand(OnNewProject);
			OpenProjectCommand = new SimpleCommand(OnOpenProject);
			SaveProjectCommand = new SimpleCommand(OnSaveProject, () => LoadedProject != null);
		}

		private void OnNewProject(object o) {
			NewProjectDialogVM result = NewProjectDialog.OpenAsDialog();
			if (!result.Success) {
				return;
			}

			ParroteerProject newProject = new ParroteerProject(result.ProjectName);
			if (result.IsTwitterImport) {
				DataSourceTwitterScrape twitterDataSource = new DataSourceTwitterScrape(result.TwitterHandle);
				newProject.SetDataSource(twitterDataSource);
				twitterDataSource.GetData();
			}

			newProject.SaveProjectData();
			LoadedProject = newProject;
		}

		private void OnOpenProject(object o) {
			OpenFileDialog openFileDialog = new OpenFileDialog() {
				Filter = "Parroteer Project | *.pj",
				InitialDirectory = ParroteerProject.RootFolder
			};

			DialogResult result = openFileDialog.ShowDialog();
			if(result == DialogResult.OK) {
				string fileName = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
				ParroteerProject project = new ParroteerProject(fileName);
				project.LoadProjectData();
				//project.GenerateText();
				LoadedProject = project;
			}
		}

		private void OnSaveProject(object o) {
			LoadedProject.SaveProjectData();
		}
	}
}
