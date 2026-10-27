namespace DisputeDesk.Application.Abstractions;

/// <summary>Sends a text message to a cellphone number (AD-21). Locally it can only reach the Mailpit inbox.</summary>
public interface ISmsSender
{
    /// <summary>Sends the message, or throws when it could not be handed over.</summary>
    Task SendAsync(string cellphone, string message, CancellationToken cancellationToken);
}
