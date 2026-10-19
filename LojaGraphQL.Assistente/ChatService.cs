using System.Diagnostics;
using System.Text.Json;

namespace LojaGraphQL.Assistente;

public sealed class ChatService(IModelClient model, LojaTools tools)
{
    public async Task<ChatResponse> AskAsync(string question, CancellationToken ct)
    {
        List<object> contents = [new { role = "user", parts = new[] { new { text = question } } }];
        List<ToolTrace> traces = [];
        for (int round = 0; round <= 3; round++)
        {
            JsonElement content = await model.GenerateAsync(contents, round < 3, ct);
            if (!content.TryGetProperty("parts", out JsonElement parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                throw new LabException(502, "O modelo devolveu um conteudo sem partes validas.");
            }
            JsonElement[] calls = parts.EnumerateArray().Where(p => p.TryGetProperty("functionCall", out _)).ToArray();
            if (calls.Length == 0)
            {
                string text = string.Join("\n", parts.EnumerateArray()
                    .Where(p => (!p.TryGetProperty("thought", out JsonElement thought) || thought.ValueKind != JsonValueKind.True)
                        && p.TryGetProperty("text", out JsonElement value) && value.ValueKind == JsonValueKind.String)
                    .Select(p => p.GetProperty("text").GetString()));
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new LabException(502, "O modelo nao forneceu uma resposta textual.");
                }
                return new ChatResponse(text, traces);
            }
            if (round == 3 || traces.Count + calls.Length > 5)
            {
                throw new LabException(502, "Limite didatico de chamadas de ferramenta atingido.");
            }
            contents.Add(content);
            List<object> results = [];
            foreach (JsonElement part in calls)
            {
                JsonElement call = part.GetProperty("functionCall");
                if (!call.TryGetProperty("name", out JsonElement nameValue) ||
                    nameValue.ValueKind != JsonValueKind.String ||
                    !call.TryGetProperty("args", out JsonElement args))
                {
                    throw new LabException(502, "O modelo solicitou uma ferramenta com contrato invalido.");
                }
                string name = nameValue.GetString()!;
                Stopwatch watch = Stopwatch.StartNew();
                object result = await tools.ExecuteAsync(name, args, ct);
                traces.Add(new ToolTrace(name, args.Clone(), result, watch.ElapsedMilliseconds));
                Dictionary<string, object> functionResponse = new()
                {
                    ["name"] = name,
                    ["response"] = new { resultado = result },
                };
                if (call.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String)
                {
                    functionResponse["id"] = id.GetString()!;
                }
                results.Add(new { functionResponse });
            }
            contents.Add(new { role = "user", parts = results });
        }
        throw new LabException(502, "Nao foi possivel concluir a resposta dentro do limite do laboratorio.");
    }
}
