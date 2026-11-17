namespace LiftX.Services;

public enum DialogKind { Confirm, Prompt, Actions }

public record ActionItem(string Label, bool Danger = false);

public class DialogRequest
{
    public DialogKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string? Message { get; init; }
    public string Ok { get; init; } = "OK";
    public string Cancel { get; init; } = "Cancel";
    public bool Danger { get; init; }
    public string Value { get; set; } = "";
    public string? Placeholder { get; init; }
    public bool Multiline { get; init; }
    public string InputMode { get; init; } = "text";
    public List<ActionItem> Actions { get; init; } = new();
    internal TaskCompletionSource<object?> Tcs { get; } = new();
}

/// <summary>App-styled replacements for confirm()/prompt() and action sheets.</summary>
public class Dialogs
{
    public DialogRequest? Current { get; private set; }
    public event Action? Changed;

    public async Task<bool> Confirm(string title, string? message = null, string ok = "OK", bool danger = false, string cancel = "Cancel")
    {
        var r = await Show(new DialogRequest { Kind = DialogKind.Confirm, Title = title, Message = message, Ok = ok, Danger = danger, Cancel = cancel });
        return r is true;
    }

    public async Task<string?> Prompt(string title, string? initial = null, string? placeholder = null, string ok = "Save",
        bool multiline = false, string inputMode = "text", string? message = null)
    {
        var r = await Show(new DialogRequest
        {
            Kind = DialogKind.Prompt, Title = title, Value = initial ?? "", Placeholder = placeholder, Ok = ok,
            Multiline = multiline, InputMode = inputMode, Message = message
        });
        return r as string;
    }

    /// <summary>Action sheet. Returns the index of the chosen item, or -1 if cancelled.</summary>
    public async Task<int> Choose(string? title, params ActionItem[] items)
    {
        var r = await Show(new DialogRequest { Kind = DialogKind.Actions, Title = title ?? "", Actions = items.ToList() });
        return r is int i ? i : -1;
    }

    private Task<object?> Show(DialogRequest req)
    {
        Current?.Tcs.TrySetResult(null);
        Current = req;
        Changed?.Invoke();
        return req.Tcs.Task;
    }

    public void Resolve(object? result)
    {
        var c = Current;
        Current = null;
        Changed?.Invoke();
        c?.Tcs.TrySetResult(result);
    }
}
