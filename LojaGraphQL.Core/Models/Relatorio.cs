namespace LojaGraphQL.Api.Models;

public enum StatusRelatorio
{
    AguardandoPublicacao,
    NaFila,
    Processando,
    Concluido,
    Falhou,
    FalhaPublicacao
}

public sealed class Relatorio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UsuarioId { get; set; }
    public StatusRelatorio Status { get; set; } = StatusRelatorio.AguardandoPublicacao;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public int Tentativas { get; set; }
    public int Conclusoes { get; set; }
    public int AtrasoSegundos { get; set; }
    public bool FalhaPermanente { get; set; }
    public string? Resultado { get; set; }
    public string? Erro { get; set; }
}
