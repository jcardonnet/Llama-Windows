using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LlamaApp.Views;

/// <summary>
/// The model details panel. Presentation only: every action forwards to
/// <see cref="ModelItemDetailsViewModel"/>, which owns the interaction state
/// and orchestrates the shell's workflows. Hosted inside the main flyout's
/// models card (MainWindow swaps it with the list) — no window or frame
/// navigation of its own.
/// </summary>
public sealed partial class ModelItemDetailsView : UserControl
{
    /// <summary>Raised by the back row; the shell swaps the list back in.</summary>
    public event EventHandler? BackRequested;

    /// <summary>The details ViewModel currently bound; null until the first show.</summary>
    public ModelItemDetailsViewModel? ViewModel { get; private set; }

    public ModelItemDetailsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Binds a new ViewModel and refreshes the compiled bindings (the view
    /// instance is reused across shows; the ViewModel is created per show).
    /// </summary>
    public void SetViewModel(ModelItemDetailsViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }

    /// <summary>Moves keyboard focus into the view when it opens (best effort).</summary>
    public void FocusFirst() => BackButton.Focus(FocusState.Programmatic);

    private void Back_Click(object sender, RoutedEventArgs e)
        => BackRequested?.Invoke(this, EventArgs.Empty);

    private void ContextOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ContextLengthOption option })
            ViewModel?.SelectContextLength(option);
    }

    private async void Chat_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await vm.ChatAsync();
    }

    private void CopyModelId_Click(object sender, RoutedEventArgs e)
        => ViewModel?.CopyModelId();

    private void BuildApiRequest_Click(object sender, RoutedEventArgs e)
        => ViewModel?.BuildApiRequest();

    private void OpenRepository_Click(object sender, RoutedEventArgs e)
        => ViewModel?.OpenRepository();

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await vm.DownloadAsync();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await vm.DeleteAsync();
    }

    /// <summary>
    /// Saves the curated per-model options. Saving rewrites the preset INI and
    /// reloads it; the router unloads a running model whose preset changed, so
    /// an active model gets an explicit confirmation first.
    /// </summary>
    private async void SaveExtraArguments_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;

        if (vm.RequiresUnloadConfirmation &&
            !await ConfirmUnloadAsync((FrameworkElement)sender))
            return;

        await vm.SaveExtraArgumentsAsync();
    }

    private static async Task<bool> ConfirmUnloadAsync(FrameworkElement anchor)
    {
        var confirmed = new TaskCompletionSource<bool>();
        var saveButton = new Button { Content = "Save and unload" };
        var cancelButton = new Button { Content = "Cancel" };
        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 10,
                MaxWidth = 280,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Save and unload this model?",
                        FontSize = 13,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = "Applying new preset options reloads the preset file, which stops the running model. "
                             + "It loads again with the new options on its next request.",
                        FontSize = 12,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancelButton, saveButton },
                    },
                },
            },
        };
        cancelButton.Click += (_, _) => { flyout.Hide(); confirmed.TrySetResult(false); };
        saveButton.Click += (_, _) => { flyout.Hide(); confirmed.TrySetResult(true); };
        flyout.Closed += (_, _) => confirmed.TrySetResult(false);
        try { flyout.ShowAt(anchor); }
        catch { return false; }
        return await confirmed.Task;
    }
}
