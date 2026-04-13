using System.ComponentModel.DataAnnotations;

namespace ImapSync.Core.Models;

public class ImapCredentials
{
    [Required(ErrorMessage = "IMAP host is required")]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535")]
    public int Port { get; set; } = 143;

    public bool UseSsl { get; set; } = false;

    [Required(ErrorMessage = "Username is required")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    public string Password { get; set; } = string.Empty;
}
