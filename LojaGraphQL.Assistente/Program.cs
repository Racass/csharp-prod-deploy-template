namespace LojaGraphQL.Assistente;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Services.AddHttpClient<LojaTools>(client =>
        {
            client.BaseAddress = new Uri(builder.Configuration["Loja:BaseUrl"] ?? "http://localhost:8080/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        });
        builder.Services.AddHttpClient<IModelClient, GeminiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
            client.MaxResponseContentBufferSize = 1024 * 1024;
        });
        builder.Services.AddHttpClient("health", client => client.Timeout = TimeSpan.FromSeconds(3));
        builder.Services.AddScoped<ChatService>();
        WebApplication app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
        app.MapGet("/api/status", async (IHttpClientFactory clients, IConfiguration config,
            CancellationToken ct) =>
        {
            bool lojaDisponivel;
            try
            {
                using HttpResponseMessage response = await clients.CreateClient("health")
                    .GetAsync(new Uri(new Uri(config["Loja:BaseUrl"] ?? "http://localhost:8080/"), "health"), ct);
                lojaDisponivel = response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                lojaDisponivel = false;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lojaDisponivel = false;
            }
            return Results.Ok(new
            {
                assistente = "Healthy",
                lojaDisponivel,
                chaveConfigurada = !string.IsNullOrWhiteSpace(config["Gemini:ApiKey"] ?? config["GEMINI_API_KEY"]),
                modelo = config["Gemini:Model"] ?? config["GEMINI_MODEL"],
                provedor = "Nao sondado: uma pergunta real verifica o acesso ao Gemini.",
            });
        });
        app.MapPost("/api/chat", async (ChatRequest request, ChatService chat,
            ILogger<Program> logger, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Pergunta) || request.Pergunta.Length > 2000)
            {
                return Results.Problem("Informe uma pergunta com ate 2000 caracteres.", statusCode: 400);
            }
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                return Results.Ok(await chat.AskAsync(request.Pergunta.Trim(), deadline.Token));
            }
            catch (LabException error)
            {
                logger.LogWarning("Chat interrompido: {Motivo}", error.Message);
                return Results.Problem(error.Message, statusCode: error.StatusCode);
            }
            catch (HttpRequestException)
            {
                logger.LogWarning("Chat interrompido por falha de conexao com uma dependencia.");
                return Results.Problem("Nao foi possivel conectar a loja ou ao Gemini.", statusCode: 502);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Chat excedeu o tempo limite.");
                return Results.Problem("Tempo limite excedido. Tente uma pergunta mais simples.", statusCode: 504);
            }
        });
        await app.RunAsync();
    }
}
