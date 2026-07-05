using System;
using System.IO;
using System.Windows.Xps.Packaging;
using System.IO.Packaging;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Xml;
using System.Printing;

namespace Hale_Marketing_International
{
    public partial class PrintPreviewWindow : Window
    {
        private FlowDocument _originalDoc;
        private FlowDocument _workingDoc;
        private FixedDocument _fixedDoc;

        public PrintPreviewWindow(FlowDocument document)
        {
            InitializeComponent();
            _originalDoc = document ?? throw new ArgumentNullException(nameof(document));

            // Clone to avoid "already has parent" error
            _workingDoc = CloneFlowDocument(_originalDoc);

            // Layout defaults (A4)
            _workingDoc.PageWidth = 816;   // ~A4 width at 96 dpi
            _workingDoc.PageHeight = 1056; // ~A4 height at 96 dpi
            _workingDoc.ColumnWidth = double.PositiveInfinity;
            _workingDoc.PagePadding = new Thickness(40);

            // Convert FlowDocument → FixedDocument
            _fixedDoc = ConvertToFixedDocument(_workingDoc);

            // Attach to viewer
            DocViewer.Document = _fixedDoc;
        }

        private FlowDocument CloneFlowDocument(FlowDocument source)
        {
            string xaml = XamlWriter.Save(source);
            using (var sr = new StringReader(xaml))
            using (var xr = XmlReader.Create(sr))
                return (FlowDocument)XamlReader.Load(xr);
        }

        // --- 🔹 Core converter: FlowDocument → FixedDocument ---
        private FixedDocument ConvertToFixedDocument(FlowDocument flowDoc)
        {
            // Create in-memory XPS
            using (var stream = new MemoryStream())
            {
                // Create package
                var package = Package.Open(stream, FileMode.Create, FileAccess.ReadWrite);
                string packUri = "pack://temp.xps";
                PackageStore.AddPackage(new Uri(packUri), package);

                // Create XPS document
                var xpsDoc = new XpsDocument(package, CompressionOption.Maximum, packUri);
                var writer = XpsDocument.CreateXpsDocumentWriter(xpsDoc);

                // Write FlowDocument paginator into XPS
                var paginator = ((IDocumentPaginatorSource)flowDoc).DocumentPaginator;
                paginator.PageSize = new Size(flowDoc.PageWidth, flowDoc.PageHeight);
                writer.Write(paginator);

                // Retrieve FixedDocument
                FixedDocumentSequence fixedSeq = xpsDoc.GetFixedDocumentSequence();
                FixedDocument fixedDoc = fixedSeq.References[0].GetDocument(false);

                // Cleanup
                xpsDoc.Close();
                PackageStore.RemovePackage(new Uri(packUri));
                package.Close();

                return fixedDoc;
            }
        }



        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Controls.PrintDialog();
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    dlg.PrintDocument(_fixedDoc.DocumentPaginator, "Invoice Print");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Print error: " + ex.Message);
                }
            }
        }

        private void BtnPrintDirect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LocalPrintServer server = new LocalPrintServer();
                PrintQueue queue = server.DefaultPrintQueue;
                var dlg = new System.Windows.Controls.PrintDialog
                {
                    PrintQueue = queue
                };
                dlg.PrintDocument(_fixedDoc.DocumentPaginator, "Invoice Print (Direct)");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Direct print error: " + ex.Message);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }

}
