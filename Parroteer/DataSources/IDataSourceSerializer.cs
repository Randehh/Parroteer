namespace Parroteer.DataSources {
	public interface IDataSourceSerializer {
		string Serialize();
		void Deserialize(string jsonPath);
	}
}
