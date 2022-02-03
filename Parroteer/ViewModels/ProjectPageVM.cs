using Parroteer.Projects;

namespace Parroteer.ViewModels {
	public class ProjectPageVM : ViewModelBase {

		private ParroteerProject m_Project;
		public ParroteerProject Project {
			get => m_Project;
			set {
				SetProperty(ref m_Project, value);
				OnPropertyChanged(nameof(DataTypeDescriptor));
			}
		}

		public string DataTypeDescriptor {
			get {
				if(Project == null || Project.DataSource == null) {
					return "";
				}

				switch (Project.DataSource.SourceType) {
					case DataSources.DataSourceTypes.TWITTER_API:
					case DataSources.DataSourceTypes.TWITTER_SCRAPER:
						return "Twitter handle:";

					case DataSources.DataSourceTypes.DISCORD:
						return "Discord server:";

					default:
						return "Unknown source";
				}
			}
		}
	}
}
