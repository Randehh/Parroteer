using Parroteer.Projects;
using Parroteer.Utilities;
using Parroteer.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Parroteer.Generation {
	public class ModelTrainer: ViewModelBase {

		private const string TRAINING_PACK_URL = @"https://drive.google.com/file/d/1dPGTYSeWGNueJtY5N6aEJlk02jHDZtlc/view?usp=sharing";

		public event EventHandler OnModelsDownloaded;
		public event EventHandler OnTrainedDataSaved;

		private ParroteerProject m_Project;
		private FileSystemWatcher m_ModelDownloadWatch;
		private FileSystemWatcher m_TrainingUpdatedWatch;

		private SimpleCommand m_DownloadTrainingPackCommand;
		public SimpleCommand DownloadTrainingPackCommand
        {
			get => m_DownloadTrainingPackCommand;
            set => SetProperty(ref m_DownloadTrainingPackCommand, value);
        }

		private float m_DownloadProgress = 0;
		public float DownloadProgress
        {
			get => m_DownloadProgress;
			set => SetProperty(ref m_DownloadProgress, value);
        }

		private string m_DownloadProgressStatus = "Standy...";
		public string DownloadProgressStatus
		{
			get => m_DownloadProgressStatus;
			set => SetProperty(ref m_DownloadProgressStatus, value);
		}

		public ModelTrainer(ParroteerProject project) {
			m_Project = project;

			DownloadTrainingPackCommand = new SimpleCommand((o) => DownloadTrainer());
		}

		public void DownloadTrainer() {
			MessageBoxResult result = MessageBox.Show("Download the training package? About 500mb download, 2gb extracted.", "Download trainer?", MessageBoxButton.YesNo);
			if (result == MessageBoxResult.Yes) {
				Task.Run(async () => {
					MemoryStream memoryStream = await GoogleDriveDownloader.DownloadFile("1dPGTYSeWGNueJtY5N6aEJlk02jHDZtlc", (progress) => {
						DownloadProgress = progress;
						DownloadProgressStatus = $"{MathF.Round(progress * 100)}%";
					});
					string zipPath = Path.Combine(m_Project.ModelTrainingFolder, "TrainingPack.zip");
					File.WriteAllBytes(zipPath, memoryStream.ToArray());

					DownloadProgressStatus = "Extracting files...";
					ZipFile.ExtractToDirectory(zipPath, m_Project.ModelTrainingFolder);

					DownloadProgressStatus = "Ready!";
				});
			}
        }

		public void StartTraining() {
			AttemptUnzipTrainer();

			string modelPath = Path.Combine(m_Project.ModelTrainingFolder, "checkpoint", "run1");
			if (!Directory.Exists(modelPath)) Directory.CreateDirectory(modelPath);

			m_ModelDownloadWatch = new FileSystemWatcher(modelPath);
			
			m_ModelDownloadWatch.EnableRaisingEvents = true;
			m_ModelDownloadWatch.Created += (o, e) => {
				OnModelsDownloaded(o, e);
				m_ModelDownloadWatch = null;
			};

			Thread trainingThread = new Thread(TrainingProcess);
			trainingThread.IsBackground = true;
			trainingThread.Name = "GPT2 Training thread";
			trainingThread.Start();
		}

		private void TrainingProcess() {
			using (Process trainingProcess = new Process()) {
				trainingProcess.StartInfo = new ProcessStartInfo(Path.Combine(m_Project.ModelTrainingFolder, "train_on_data.bat"), $"{m_Project.DataSourceSimplePath} {m_Project.ModelTrainingFolder}") {
					CreateNoWindow = false,
					UseShellExecute = false,
				};
				trainingProcess.Start();
				trainingProcess.WaitForExit();
			}
		}

		public void GenerateText() {
			Thread generateThread = new Thread(GenerateTextProcess);
			generateThread.IsBackground = true;
			generateThread.Name = "GPT2 Generator thread";
			generateThread.Start();
		}

		public void GenerateTextProcess() {
			using (Process generateProcess = new Process()) {
				generateProcess.StartInfo = new ProcessStartInfo(Path.Combine(m_Project.ModelTrainingFolder, "generate_text.bat"), $"{Path.Combine(m_Project.ResultsFolder, "hello.txt")} {m_Project.ModelTrainingFolder}") {
					CreateNoWindow = false,
					UseShellExecute = false,
				};
				generateProcess.Start();
				generateProcess.WaitForExit();
			}
		}

		public void AttemptUnzipTrainer() {
			if(Directory.GetFiles(m_Project.ModelTrainingFolder).Length != 0) {
				return;
			}

			ZipFile.ExtractToDirectory("Resources\\GPT2Training.zip", m_Project.ModelTrainingFolder);
		}
	}
}
