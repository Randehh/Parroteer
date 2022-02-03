using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Parroteer.DataSources {
	public interface IDataSource {
		DataSourceStatuses Status { get; }
		DataSourceTypes SourceType { get; }
		IDataSourceSerializer Serializer { get; }
		string FileName { get; }
		ObservableCollection<string> DataLines { get; set; }
		string SourceID { get; set; }

		void GetData();
		bool IsRetrievingData { get; set; }
		float DataFetchProgress { get; set; }

		event EventHandler OnDataReceivedEvent;
		void OnDataReceived();

		string SaveData();

		void LoadData(string json);
		string SaveDataLinesAsSimple();
	}
}