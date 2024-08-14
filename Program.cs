using iText.Kernel.Pdf.Canvas.Parser;
using PdfSharp.Pdf.IO;
using System.Drawing;
using System.Drawing.Imaging;
using PdfDocument = PdfSharp.Pdf.PdfDocument;
using PdfPage = PdfSharp.Pdf.PdfPage;
using PdfReader = PdfSharp.Pdf.IO.PdfReader;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Net.Http.Json;


#pragma warning disable CA1416 // Validate platform compatibility
public class PageData
{
    public int PageNumber { get; set; }
    public required string Text { get; set; }
    public required string ImagePaths { get; set; }
}

public class TextCorrectionService
{
    private readonly HttpClient _httpClient;

    public TextCorrectionService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GetCorrectedTextAsync(string text, int pageNumber)
    {
        try
        {
            var requestContent = new
            {
                text = $@"Você é um corretor ortográfico de Português Brasil, revise o texto preservando a formatação original e melhorando a legibilidade, seguindo as seguintes regras:
                - Não adicione nenhum texto de resposta além da correção, envie apenas a correção;
                - O texto foi retirado do manual do novo Peugeot 208;
                - Corrija as listas que tiverem em formatação errada;
                - O que for sumário, corrija de forma que fique com formatação de sumário, mas não envie nada além da correção;
                - Remova todo hífen ('-') que você encontrar durante a transcrição, juntando as palavras também;
                - Caso não consiga corrigir algo, apenas aponte a parte não corrigida com a frase: 'Não entendi essa etapa do texto';

                Faça isso no seguinte texto:
                {text}"
            };

            var response = await _httpClient.PostAsJsonAsync("http://localhost:3000/ia", requestContent);

            response.EnsureSuccessStatusCode();
            Console.WriteLine($"Page {pageNumber} Done");

            var correctedText = await response.Content.ReadAsStringAsync();
            return correctedText;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error with API: {e.Message}");
            return text;
        }
    }
}


static class Program
{
    static async Task Main()
    {
        string pdfPath = "Peugeot 2008 - 2024.pdf";
        string outputCsv = "output.csv";
        string outputFolder = "images";

        Directory.CreateDirectory(outputFolder);

        using (var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) } )
        {
            var textCorrectionService = new TextCorrectionService(httpClient);

            // Processar o PDF
            await PdfToTextAndImagesAsync(pdfPath, outputCsv, outputFolder, textCorrectionService);
        }
    }

    // Extraindo o texto usando iTextSharp
    static string ExtractTextFromPage(string pdfPath, int pageNumber)
    {
        using (var reader = new iText.Kernel.Pdf.PdfReader(pdfPath))
        {
            using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader))
            {
                var page = pdfDoc.GetPage(pageNumber);
                return PdfTextExtractor.GetTextFromPage(page);
            }
        }
    }

    // Salvando a página como imagem
    static string SavePageAsJpg(PdfSharp.Pdf.PdfPage page, int pageId, string outputFolder)
    {
        string imagePath = Path.Combine(outputFolder, $"page_{pageId}.jpg");

        // Exemplo simplificado que cria um bitmap branco:
        using (var bitmap = new Bitmap((int)page.Width.Point, (int)page.Height.Point))
        {

            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                // Adicionar lógica para desenhar o conteúdo da página no bitmap
            }

            bitmap.Save(imagePath, ImageFormat.Jpeg);
        }
        return imagePath;
    }

    // Processar o PDF, extraindo texto e salvando páginas como imagens
    static async Task PdfToTextAndImagesAsync(string pdfPath, string outputCsv, string outputFolder, TextCorrectionService textCorrectionService)
    {
        try
        {
            PdfDocument pdfDocument = PdfReader.Open(pdfPath, PdfDocumentOpenMode.ReadOnly);

            for (int pageNum = 1; pageNum < pdfDocument.PageCount; pageNum++)
            {

                //if(pageNum < 108) continue; // Pula as páginas pré 108
                PdfPage page = pdfDocument.Pages[pageNum];
                string text = ExtractTextFromPage(pdfPath, pageNum);
                text = CleanText(text);

                string correctedText = await textCorrectionService.GetCorrectedTextAsync(text, pageNum);

                string pageImagePath = SavePageAsJpg(page, pageNum, outputFolder);

                var data = new PageData
                {
                    PageNumber = pageNum,
                    Text = correctedText.Replace("\n", Environment.NewLine),
                    ImagePaths = Path.GetFileName(pageImagePath)
                };

                AppendToCsv(data, outputCsv);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error processing PDF file: {e}");
        }
    }


    // Limpar o texto
    static string CleanText(string text)
    {
        return text.Trim(); // Simplesmente remove espaços em branco no início e no final
    }

    // Adicionar dados ao CSV
    static void AppendToCsv(PageData data, string outputCsv)
    {
        bool fileExists = File.Exists(outputCsv);

        using (var writer = new StreamWriter(outputCsv, append: true))
        using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            NewLine = Environment.NewLine, // Define o delimitador de linha como o padrão do sistema
            Quote = '"', // Garantir que aspas são usadas corretamente
            Escape = '"', // Configura o caractere de escape como aspas duplas
        }))
        {
            if (!fileExists)
            {
                csv.WriteHeader<PageData>();
                csv.NextRecord();
            }

            data.Text = data.Text.Replace("\\n", "\n"); // Substitui \n por quebra de linha real

            csv.WriteRecord(data);
            csv.NextRecord();
        }
    }

}
#pragma warning restore CA1416 // Validate platform compatibility