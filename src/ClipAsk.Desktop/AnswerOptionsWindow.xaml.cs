using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ClipAsk.Core.Providers;

namespace ClipAsk.Desktop;

internal partial class AnswerOptionsWindow : Window
{
    public AnswerOptionsWindow(string instruction, string? selectedModel, CodexModelSelection? automaticModel, IReadOnlyList<CodexModelSelection> models)
    {
        InitializeComponent();
        var automaticLabel = automaticModel is null
            ? "Automatic (resolved when analyzing)"
            : $"Automatic — {automaticModel.DisplayName} · {automaticModel.ReasoningEffort}";
        var choices = new[] { new ModelChoice(null, automaticLabel) }
            .Concat(models.Select(model => new ModelChoice(model.Model, $"{model.DisplayName} · {model.ReasoningEffort}")))
            .ToArray();
        ModelBox.ItemsSource = choices;
        ModelBox.SelectedItem = choices.FirstOrDefault(choice => choice.Model == selectedModel) ?? choices[0];
        ModelHelpText.Text = automaticModel is null
            ? "Automatic resolves against the image-capable models advertised by your ChatGPT account."
            : automaticModel.Model == CodexPolicy.PreferredModel && automaticModel.ReasoningEffort == CodexPolicy.PreferredReasoningEffort
                ? "Automatic prefers GPT-5.6 Terra with low reasoning when your account advertises it for images; otherwise it uses the advertised default."
                : "GPT-5.6 Terra with low reasoning is not eligible on this account, so Automatic uses the advertised image-capable default.";
        InstructionBox.Text = instruction;
        SourceInitialized += (_, _) => NativeMethods.EnableRoundedCorners(this);
        Loaded += (_, _) => InstructionBox.Focus();
    }

    public string Instruction => InstructionBox.Text.Trim();
    public string? SelectedModel => (ModelBox.SelectedItem as ModelChoice)?.Model;

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
}
