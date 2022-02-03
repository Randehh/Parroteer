using Parroteer.DataSources;
using System;
using System.IO;
using System.Text.Json;

namespace Parroteer.Projects {
	public class ParotteerProjectSerializer {

		private ParroteerProject m_Project;

		public ParotteerProjectSerializer(ParroteerProject project) {
			m_Project = project;
		}

		public void Save() {
			ParroteerProjectData data = new ParroteerProjectData() {
				DataSourceType = m_Project.DataSource.SourceType.ToString(),
				DataSourceDataPath = m_Project.DataSourceDataPath,
			};

			string jsonData = JsonSerializer.Serialize(data);
			File.WriteAllText(m_Project.ProjectFilePath, jsonData);

			string dataSourceJson = m_Project.DataSource.SaveData();
			File.WriteAllText(m_Project.DataSourceDataPath, dataSourceJson);

			string dataSourceSimpleJson = m_Project.DataSource.SaveDataLinesAsSimple();
			File.WriteAllText(m_Project.DataSourceSimplePath, dataSourceSimpleJson);
		}

		public void Load() {
			string jsonData = File.ReadAllText(m_Project.ProjectFilePath);
			ParroteerProjectData data = JsonSerializer.Deserialize<ParroteerProjectData>(jsonData);

			if (!string.IsNullOrEmpty(data.DataSourceType)) {
				IDataSource dataSource = null;
				switch (Enum.Parse(typeof(DataSourceTypes), data.DataSourceType)) {
					case DataSourceTypes.TWITTER_API:
						break;
					case DataSourceTypes.TWITTER_SCRAPER:
						dataSource = new DataSourceTwitterScrape("");
						m_Project.SetDataSource(dataSource);
						break;
					case DataSourceTypes.DISCORD:
						break;
					default:
						break;
				}

				if (dataSource != null) {
					dataSource.LoadData(data.DataSourceDataPath);
				}
			}
		}

		private class ParroteerProjectData {
			public string DataSourceType { get; set; } = "";
			public string DataSourceDataPath { get; set; } = "";
		}
	}
}
