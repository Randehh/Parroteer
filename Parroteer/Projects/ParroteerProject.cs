using Parroteer.DataSources;
using Parroteer.Generation;
using Parroteer.ViewModels;
using System;
using System.IO;

namespace Parroteer.Projects {
	public class ParroteerProject : ViewModelBase {

		private string m_ProjectName = "";
		public string ProjectName {
			get => m_ProjectName;
			set => SetProperty(ref m_ProjectName, value);
		}

		public static string RootFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Parroteer");
		public string ProjectFolder => Path.Combine(RootFolder, ProjectName);
		public string ProjectFilePath => Path.Combine(ProjectFolder, $"{ProjectName}.pj");
		public string DataSourceFolder => Path.Combine(ProjectFolder, "DataSource");
		public string DataSourceDataPath => Path.Combine(DataSourceFolder, DataSource.FileName);
		public string DataSourceSimplePath => Path.Combine(DataSourceFolder, "SimpleData.txt");
		public string ModelTrainingFolder => Path.Combine(ProjectFolder, "ModelTraining");

		public string ResultsFolder => Path.Combine(ProjectFolder, "Results");

		private ParotteerProjectSerializer m_Serializer;

		private IDataSource m_DataSource;
		public IDataSource DataSource {
			get => m_DataSource;
			set {
				SetProperty(ref m_DataSource, value);
				OnPropertyChanged(nameof(DataSourceString));
				OnPropertyChanged(nameof(CanAddDataSource));
			}
		}
		public string DataSourceString => DataSource.SourceType.ToString();
		public bool CanAddDataSource => DataSource == null;

		private ModelTrainer m_ModelTrainer;
		public ModelTrainer ModelTrainer
        {
			get => m_ModelTrainer;
			set => SetProperty(ref m_ModelTrainer, value);
        }

		public ParroteerProject(string projectName) {
			ProjectName = projectName;

			m_Serializer = new ParotteerProjectSerializer(this);
			m_ModelTrainer = new ModelTrainer(this);
		}

		public void SaveProjectData() {
			CreateProjectFolders();
			m_Serializer.Save();
		}

		public void LoadProjectData() {
			m_Serializer.Load();
		}

		public bool SetDataSource(IDataSource source) {
			if(DataSource != null) {
				return false;
			}

			DataSource = source;
			return true;
		}

		public void StartTraining() {
			m_ModelTrainer.StartTraining();
		}

		public void GenerateText() {
			m_ModelTrainer.GenerateText();
		}

		private void CreateProjectFolders() {
			string[] directoriesToCreate = new string[] {
				RootFolder,
				ProjectFolder,
				DataSourceFolder,
				ModelTrainingFolder,
				ResultsFolder
			};

			foreach (string directory in directoriesToCreate) {
				if (!Directory.Exists(directory)) {
					Directory.CreateDirectory(directory);
				}
			}
		}
	}
}
