using LiftX.Services;
using Microsoft.AspNetCore.Components;

namespace LiftX.Components;

/// <summary>Base for components that re-render whenever AppState changes.</summary>
public abstract class StateComponent : ComponentBase, IDisposable
{
    [Inject] protected AppState S { get; set; } = default!;

    protected override void OnInitialized()
    {
        S.Changed += OnStateChanged;
        base.OnInitialized();
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        S.Changed -= OnStateChanged;
        GC.SuppressFinalize(this);
    }
}
