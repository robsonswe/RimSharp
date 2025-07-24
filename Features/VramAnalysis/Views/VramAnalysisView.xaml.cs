// --- Features/VramAnalysis/Views/VramAnalysisView.xaml.cs ---
using System.Windows.Controls;
using System.Windows.Controls.Primitives; // For ButtonBase
using System.Diagnostics; // For Debug.WriteLine
using RimSharp.Features.VramAnalysis.ViewModels; // To access VramAnalysisViewModel
using System.Linq; // MODIFIED: Added for LINQ extension methods

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
                if (header.Column?.Header is Panel headerContent)
                {
                    var taggedElement = headerContent.Children.OfType<TextBlock>().FirstOrDefault(tb => tb.Tag != null);
                    if (taggedElement?.Tag is string sortPropertyName)
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
                        Debug.WriteLine("[VRAM View] Could not find a TextBlock with a Tag in the header content Panel.");
                    }
                }
                else
                {
                    Debug.WriteLine("[VRAM View] Header content is not a Panel or is null.");
                }
            }
            else
            {
                Debug.WriteLine("[VRAM View] Sender is not GridViewColumnHeader.");
            }
        }
    }
}