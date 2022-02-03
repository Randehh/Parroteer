using Parroteer.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Parroteer.DataSources {
	public abstract class DataSourceBase : ViewModelBase, IDataSource {
		public DataSourceStatuses Status {
			get {
				if (IsRetrievingData) return DataSourceStatuses.IN_PROGRESS;
				if (DataLines.Count == 0) return DataSourceStatuses.EMPTY;
				return DataSourceStatuses.READY;
			}
		}
		public abstract DataSourceTypes SourceType { get; }
		public abstract IDataSourceSerializer Serializer { get; set; }
		public abstract string FileName { get; }
		public ObservableCollection<string> DataLines { get; set; } = new ObservableCollection<string>();
		public string SourceID { get; set; }

		public event EventHandler OnDataReceivedEvent;

		public DataSourceBase() {

		}

		public virtual void GetData() {
			IsRetrievingData = true;
		}

		private bool m_IsRetrievingData = false;
		public bool IsRetrievingData {
			get => m_IsRetrievingData;
			set {
				SetProperty(ref m_IsRetrievingData, value);
				OnPropertyChanged(nameof(Status));
			}
		}

		private float m_DataFetchProgress = 0;
		public float DataFetchProgress {
			get => m_DataFetchProgress;
			set {
				SetProperty(ref m_DataFetchProgress, value);
			}
		}

		public virtual void OnDataReceived() {
			IsRetrievingData = false;
			OnPropertyChanged(nameof(Status));

			OnDataReceivedEvent?.Invoke(this, new EventArgs());
		}

		public string SaveData() {
			string jsonData = Serializer.Serialize();
			return jsonData;
		}

		public void LoadData(string jsonPath) {
			Serializer.Deserialize(jsonPath);
		}

		public string SaveDataLinesAsSimple() {
			StringBuilder sb = new StringBuilder();
			foreach (string tweet in DataLines) {
				sb.AppendLine($"<|start|>{tweet}<|end|>");
			}
			return sb.ToString();
		}
	}
}
