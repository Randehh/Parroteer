using Parroteer.ViewModels;
using System.Windows;

namespace Parroteer.Views {
	/// <summary>
	/// Interaction logic for NewProjectDialog.xaml
	/// </summary>
	public partial class NewProjectDialog : Window {

		public NewProjectDialogVM ViewModel { get; private set; }

		public NewProjectDialog() {
			InitializeComponent();
			DataContext = ViewModel = new NewProjectDialogVM();
			ViewModel.OnRequestClose += ViewModel_OnRequestClose;
		}

		private void ViewModel_OnRequestClose(object sender, System.EventArgs e) {
			this.Close();
		}

		public static NewProjectDialogVM OpenAsDialog() {
			NewProjectDialog dialog = new NewProjectDialog();
			dialog.ShowDialog();
			return dialog.ViewModel;
		}
	}
}
