using iText.Kernel.Pdf.Canvas.Parser;
using PdfSharp.Pdf.IO;
using PdfSharpDocument = PdfSharp.Pdf.PdfDocument;
using PdfSharpPage = PdfSharp.Pdf.PdfPage;
using PdfSharpReader = PdfSharp.Pdf.IO.PdfReader;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Net.Http.Json;
using System.Diagnostics;


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

    static string SavePageAsJpg(string pdfPath, int pageNumber, string outputFolder)
    {
        // Caminho para o executável do Ghostscript
        string ghostscriptPath = @"C:\Program Files\gs\gs10.03.1\bin\gswin64c.exe"; // Ajuste o caminho conforme necessário

        // Caminho de saída da imagem
        string imagePath = $"page_{pageNumber}.png";
        string outputPath = Path.Combine(outputFolder, imagePath);

        // Argumentos para o comando do Ghostscript
        string arguments = $"-dSAFER -dBATCH -dNOPAUSE -sDEVICE=png16m -r300 -dFirstPage={pageNumber} -dLastPage={pageNumber} -sOutputFile=\"{outputPath}\" \"{pdfPath}\"";

        // Configura o processo para chamar o Ghostscript
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = ghostscriptPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        #pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
        using (Process process = Process.Start(startInfo))
        {
            // Aguarda a conclusão do processo
            #pragma warning disable CS8602 // Dereference of a possibly null reference.
            process.WaitForExit();
            #pragma warning restore CS8602 // Dereference of a possibly null reference.

            // Lê a saída padrão e a saída de erro
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            // Exibe a saída e a saída de erro para depuração
            if (!string.IsNullOrEmpty(output))
                Console.WriteLine("Output: " + output);
            if (!string.IsNullOrEmpty(error))
                Console.WriteLine("Error: " + error);
        }
        #pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
        return imagePath;
    }

    // Processar o PDF, extraindo texto e salvando páginas como imagens
    static async Task PdfToTextAndImagesAsync(string pdfPath, string outputCsv, string outputFolder, TextCorrectionService textCorrectionService)
    {
        try
        {
            PdfSharpDocument pdfDocument = PdfSharpReader.Open(pdfPath, PdfDocumentOpenMode.ReadOnly);

            for (int pageNum = 1; pageNum < pdfDocument.PageCount; pageNum++)
            {

                
                PdfSharpPage page = pdfDocument.Pages[pageNum];
                string text = ExtractTextFromPage(pdfPath, pageNum);
                text = CleanText(text);
                string pageImagePath = SavePageAsJpg(pdfPath, pageNum, outputFolder);
                string correctedText = await textCorrectionService.GetCorrectedTextAsync(text, pageNum);

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