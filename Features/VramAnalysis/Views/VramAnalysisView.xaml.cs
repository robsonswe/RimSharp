// --- Features/VramAnalysis/Views/VramAnalysisView.xaml.cs ---
using System.Windows.Controls;
using System.Windows.Controls.Primitives; // For ButtonBase
using System.Diagnostics; // For Debug.WriteLine
using RimSharp.Features.VramAnalysis.ViewModels; // To access VramAnalysisViewModel

namespace RimSharp.Features.VramAnalysis.Views
{
    public partial class VramAnalysisView : UserControl
    {
        public VramAnalysisView()
        {
            InitializeComponent();
        }

        private void GridViewColumnHeader_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is GridViewColumnHeader header)
            {
                // The Header property of the GridViewColumn itself contains the TextBlock.
                // We need to access that TextBlock to get its Tag.
                if (header.Column?.Header is TextBlock headerContent && headerContent.Tag is string sortPropertyName)
                {
                    // Ensure the DataContext is the VramAnalysisViewModel
                    if (DataContext is VramAnalysisViewModel viewModel)
                    {
                        Debug.WriteLine($"[VRAM View] Column header clicked: {sortPropertyName}");
                        // Execute the SortCommand in the ViewModel
                        viewModel.SortCommand.Execute(sortPropertyName);
                    }
                    else
                    {
                        Debug.WriteLine("[VRAM View] DataContext is not VramAnalysisViewModel.");
                    }
                }
                else
                {
                    Debug.WriteLine("[VRAM View] Header content is not a TextBlock or Tag is missing/invalid.");
                }
            }
            else
            {
                Debug.WriteLine("[VRAM View] Sender is not GridViewColumnHeader.");
            }
        }
    }
}