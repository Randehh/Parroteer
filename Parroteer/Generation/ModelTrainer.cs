using Parroteer.Projects;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace Parroteer.Generation {
	public class ModelTrainer {

		public event EventHandler OnModelsDownloaded;
		public event EventHandler OnTrainedDataSaved;

		private ParroteerProject m_Project;
		private FileSystemWatcher m_ModelDownloadWatch;
		private FileSystemWatcher m_TrainingUpdatedWatch;

		public ModelTrainer(ParroteerProject project) {
			m_Project = project;
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
