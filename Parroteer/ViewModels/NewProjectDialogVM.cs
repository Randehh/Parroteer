using Parroteer.Utilities;
using System;
using System.Windows;

namespace Parroteer.ViewModels {
	public class NewProjectDialogVM : ViewModelBase {

		public event EventHandler OnRequestClose;

		private bool m_Success = false;
		public bool Success {
			get => m_Success;
			set => SetProperty(ref m_Success, value);
		}

		private string m_ProjectName = "";
		public string ProjectName {
			get => m_ProjectName;
			set {
				SetProperty(ref m_ProjectName, value);
				OnPropertyChanged(nameof(CreateCommand));
			}
		}

		private SimpleCommand m_CreateCommand;
		public SimpleCommand CreateCommand {
			get => m_CreateCommand;
			set => SetProperty(ref m_CreateCommand, value);
		}

		private string[] m_ImportTypes = new string[] {
			"Twitter",
			"Discord"
		};
		public string[] ImportTypes => m_ImportTypes;

		private string m_SelectedImportType = "Twitter";
		public string SelectedImportType {
			get => m_SelectedImportType;
			set {
				SetProperty(ref m_SelectedImportType, value);
				OnPropertyChanged(nameof(IsTwitterImport));
				OnPropertyChanged(nameof(IsDiscordImport));
				OnPropertyChanged(nameof(TwitterSectionVisiblity));
				OnPropertyChanged(nameof(DiscordSectionVisiblity));
				OnPropertyChanged(nameof(CreateCommand));
			}
		}

		public bool IsTwitterImport => m_SelectedImportType == "Twitter";
		public bool IsDiscordImport => m_SelectedImportType == "Discord";
		public Visibility TwitterSectionVisiblity => IsTwitterImport ? Visibility.Visible : Visibility.Collapsed;
		public Visibility DiscordSectionVisiblity => IsDiscordImport ? Visibility.Visible : Visibility.Collapsed;

		private string m_TwitterHandle = "";
		public string TwitterHandle {
			get => m_TwitterHandle;
			set {
				SetProperty(ref m_TwitterHandle, value);
				OnPropertyChanged(nameof(CreateCommand));
			}
		}

		private string m_DiscordDumpPath = "";
		public string DiscordDumpPath {
			get => m_DiscordDumpPath;
			set {
				SetProperty(ref m_DiscordDumpPath, value);
				OnPropertyChanged(nameof(CreateCommand));
			}
		}

		private SimpleCommand m_BrowseDiscordPathCommand;
		public SimpleCommand BrowseDiscordPathCommand {
			get => m_BrowseDiscordPathCommand;
			set => SetProperty(ref m_BrowseDiscordPathCommand, value);
		}

		public NewProjectDialogVM() {
			BrowseDiscordPathCommand = new SimpleCommand(SelectDiscordDumpFile);
			CreateCommand = new SimpleCommand(CreateProject, CanCreateProject);
		}

		private void SelectDiscordDumpFile(object o) {
			Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
			bool? result = openFileDialog.ShowDialog();
			if (result.HasValue && result.Value == true) {
				DiscordDumpPath = openFileDialog.FileName;
			}
		}

		private void CreateProject(object o) {
			Success = true;
			OnRequestClose(this, new EventArgs());
		}

		private bool CanCreateProject() {
			if (string.IsNullOrEmpty(ProjectName)) {
				return false;
			}

			if (IsTwitterImport) {
				if (string.IsNullOrEmpty(TwitterHandle)) {
					return false;
				}
			} else if (IsDiscordImport) {
				return false; //Unsupported
			}

			return true;
		}
	}
}
