using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Screenshot.Core.Providers;

namespace Screenshot.Desktop;

internal partial class AnswerOptionsWindow : Window
{
    public AnswerOptionsWindow(string instruction, string? selectedModel, IReadOnlyList<CodexModelSelection> models)
    {
        InitializeComponent();
        var choices = new[] { new ModelChoice(null, "Automatic (recommended)") }
            .Concat(models.Select(model => new ModelChoice(model.Model, $"{model.DisplayName} · {model.ReasoningEffort}")))
            .ToArray();
        ModelBox.ItemsSource = choices;
        ModelBox.SelectedItem = choices.FirstOrDefault(choice => choice.Model == selectedModel) ?? choices[0];
        InstructionBox.Text = instruction;
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
