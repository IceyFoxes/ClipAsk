using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ClipAsk.Core.Providers;

namespace ClipAsk.Desktop;

internal partial class AnswerOptionsWindow : Window
{
    private readonly CodexModelSelection? automaticModel;
    private readonly IReadOnlyList<CodexModelSelection> models;
    private string? requestedEffort;

    public AnswerOptionsWindow(
        string instruction,
        string? selectedModel,
        string? selectedReasoningEffort,
        CodexModelSelection? automaticModel,
        IReadOnlyList<CodexModelSelection> models,
        bool fastMode)
    {
        InitializeComponent();
        this.automaticModel = automaticModel;
        this.models = models;
        requestedEffort = selectedReasoningEffort;
        var automaticLabel = automaticModel is null
            ? "Automatic (resolved when analyzing)"
            : $"Automatic — {automaticModel.DisplayName}";
        var choices = new[] { new ModelChoice(null, automaticLabel) }
            .Concat(models.Select(model => new ModelChoice(model.Model, model.DisplayName)))
            .ToArray();
        ModelBox.ItemsSource = choices;
        ModelBox.SelectedItem = choices.FirstOrDefault(choice => choice.Model == selectedModel) ?? choices[0];
        ModelHelpText.Text = automaticModel is null
            ? "Automatic resolves against the image-capable models advertised by your ChatGPT account."
            : automaticModel.Model == CodexPolicy.PreferredModel && automaticModel.ReasoningEffort == CodexPolicy.PreferredReasoningEffort
                ? "Automatic prefers GPT-6 Luna with low reasoning when your account advertises it for images; otherwise it uses the advertised default."
                : "GPT-6 Luna with low reasoning is not eligible on this account, so Automatic uses the advertised image-capable default.";
        RefreshEffortChoices();
        FastModeBox.IsChecked = fastMode;
        InstructionBox.Text = instruction;
        SourceInitialized += (_, _) => NativeMethods.EnableRoundedCorners(this);
        Loaded += (_, _) => InstructionBox.Focus();
    }

    public string Instruction => InstructionBox.Text.Trim();
    public string? SelectedModel => (ModelBox.SelectedItem as ModelChoice)?.Model;
    public string? SelectedReasoningEffort => (EffortBox.SelectedItem as EffortChoice)?.Effort;
    public bool FastMode => FastModeBox.IsChecked == true;

    private void ModelSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => RefreshEffortChoices();

    private void EffortSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        requestedEffort = (EffortBox.SelectedItem as EffortChoice)?.Effort;

    private void RefreshEffortChoices()
    {
        if (EffortBox is null)
            return;
        var desiredEffort = requestedEffort;
        var selectedModel = (ModelBox.SelectedItem as ModelChoice)?.Model;
        var selection = selectedModel is null
            ? automaticModel
            : models.FirstOrDefault(model => model.Model == selectedModel);
        var automaticLabel = selection is null
            ? "Automatic (resolved when analyzing)"
            : $"Automatic — {FormatEffort(selection.ReasoningEffort)}";
        var choices = new[] { new EffortChoice(null, automaticLabel) }
            .Concat((selection?.ReasoningEfforts ?? Array.Empty<string>())
                .Select(effort => new EffortChoice(effort, FormatEffort(effort))))
            .ToArray();
        EffortBox.ItemsSource = choices;
        EffortBox.SelectedItem = choices.FirstOrDefault(choice => choice.Effort == desiredEffort) ?? choices[0];
        requestedEffort = (EffortBox.SelectedItem as EffortChoice)?.Effort;
    }

    private static string FormatEffort(string effort) =>
        string.IsNullOrEmpty(effort) ? effort : char.ToUpperInvariant(effort[0]) + effort[1..];

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e) => Close();

    private void HeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private sealed record ModelChoice(string? Model, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record EffortChoice(string? Effort, string Label)
    {
        public override string ToString() => Label;
    }
}
