using System.ComponentModel.DataAnnotations;

namespace Nook.Api.Models;

public class Office
{
    public int Id { get; set; }

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    public int Ordinal { get; set; }

    public List<Workspace> Workspaces { get; set; } = [];
}
