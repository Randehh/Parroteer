using Parroteer.ViewModels;
using System.Windows.Controls;

namespace Parroteer.Views {
	/// <summary>
	/// Interaction logic for LandingPage.xaml
	/// </summary>
	public partial class LandingPage : UserControl {
		public LandingPage() {
			InitializeComponent();
			DataContext = new LandingPageVM();
		}
	}
}
