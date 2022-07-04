using Parroteer.Projects;
using Parroteer.Utilities;
using Parroteer.ViewModels;
using System;
using System.Collections.ObjectModel;
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

		public event EventHandler OnFileCreated;
		public event EventHandler OnTrainedDataSaved;

		private ParroteerProject m_Project;
		private FileSystemWatcher m_FileCreatedWatch;
		private FileSystemWatcher m_TrainingUpdatedWatch;

		private ObservableCollection<string> m_TrainingLog = new ObservableCollection<string>();
		public ObservableCollection<string> TrainingLog
        {
			get => m_TrainingLog;
			set => SetProperty(ref m_TrainingLog, value);
        }

		private string m_PythonPath;
		public string PythonPath
        {
			get
            {
                if (string.IsNullOrEmpty(m_PythonPath)) {
					bool isInstalled = PythonUtilities.GetPythonPath(out m_PythonPath);
					StepReadyTwo = isInstalled;
				}
				return m_PythonPath;
            }
        }

		public bool IsPythonInstalled => !string.IsNullOrEmpty(PythonPath);

		public string PythonStatus => $"Python status: {(IsPythonInstalled ? "installed" : "not installed")}";

		private SimpleCommand m_DownloadTrainingPackCommand;
		public SimpleCommand DownloadTrainingPackCommand
        {
			get => m_DownloadTrainingPackCommand;
            set => SetProperty(ref m_DownloadTrainingPackCommand, value);
        }

		private SimpleCommand m_StartTrainingCommand;
		public SimpleCommand StartTrainingCommand
		{
			get => m_StartTrainingCommand;
			set => SetProperty(ref m_StartTrainingCommand, value);
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

		private bool m_StepReadyTwo = false;
		public bool StepReadyTwo
        {
			get => m_StepReadyTwo;
			set => SetProperty(ref m_StepReadyTwo, value);
        }

		private bool m_StepReadyThree = false;
		public bool StepReadyThree
		{
			get => m_StepReadyThree;
			set => SetProperty(ref m_StepReadyThree, value);
		}

		public ModelTrainer(ParroteerProject project) {
			m_Project = project;

			DownloadTrainingPackCommand = new SimpleCommand((o) => DownloadTrainer());
			StartTrainingCommand = new SimpleCommand((o) => StartTraining());

			StepReadyTwo = IsPythonInstalled;
			StepReadyThree = IsTrainerDownloaded();
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
					
					DownloadProgressStatus = "Deleting zip file...";
					File.Delete(zipPath);

					DownloadProgressStatus = "Adjusting files...";
					string configPath = Path.Combine(m_Project.ModelTrainingFolder, "gpt2_env", "pyvenv.cfg");
					string[] configContent = await File.ReadAllLinesAsync(configPath);
					configContent[0] = $"home = {PythonPath}";
					await File.WriteAllLinesAsync(configPath, configContent);

					DownloadProgressStatus = "Ready!";

					Application.Current.Dispatcher.Invoke(() => {
						StepReadyThree = true;
					});
				});
			}
        }

		public bool IsTrainerDownloaded() {
			string modelsPath = Path.Combine(m_Project.ModelTrainingFolder, "models");
			return Directory.Exists(modelsPath);
        }

		public void StartTraining() {

			string modelPath = Path.Combine(m_Project.ModelTrainingFolder, "checkpoint", "run1");
			if (!Directory.Exists(modelPath)) Directory.CreateDirectory(modelPath);

			m_FileCreatedWatch = new FileSystemWatcher(modelPath);
			
			m_FileCreatedWatch.EnableRaisingEvents = true;
			m_FileCreatedWatch.Created += (o, e) => {
				OnFileCreated?.Invoke(o, e);

				Application.Current.Dispatcher.Invoke(() => {
					TrainingLog.Add($"File created: {e.Name}");
				});
			};

			Thread trainingThread = new Thread(TrainingProcess);
			trainingThread.IsBackground = true;
			trainingThread.Name = "GPT2 Training thread";
			trainingThread.Start();
		}

		private void TrainingProcess() {
			using (Process trainingProcess = new Process()) {
				trainingProcess.StartInfo = new ProcessStartInfo(Path.Combine(m_Project.ModelTrainingFolder, "train_on_data.bat"), $"{m_Project.DataSourceSimplePath} {m_Project.ModelTrainingFolder}") {
					CreateNoWindow = true,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
				};
				trainingProcess.EnableRaisingEvents |= true;
                trainingProcess.OutputDataReceived += OnTrainingMessageStandard;
				trainingProcess.ErrorDataReceived += OnTrainingMessageError;
				trainingProcess.Start();
				trainingProcess.BeginOutputReadLine();
				trainingProcess.BeginErrorReadLine();
				trainingProcess.WaitForExit();
			}
		}

        private void OnTrainingMessageStandard(object sender, DataReceivedEventArgs e) {
			string message = e.Data;
            if (string.IsNullOrEmpty(message)) {
				return;
            }

			OnTrainingMessage(message);
		}

		private void OnTrainingMessageError(object sender, DataReceivedEventArgs e) {
			string message = e.Data;
			if (string.IsNullOrEmpty(message)) {
				return;
			}

			OnTrainingMessage($"Error: {message}");
		}

		private void OnTrainingMessage(string message) {
			Application.Current.Dispatcher.Invoke(() => {
				TrainingLog.Add(message);
			});
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
	}
}
