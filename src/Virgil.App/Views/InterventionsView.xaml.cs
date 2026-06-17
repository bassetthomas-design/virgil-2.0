using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Virgil.App.Controls;
using Virgil.Core.Interventions;
using Virgil.Domain;

namespace Virgil.App.Views;

public partial class InterventionsView : UserControl
{
    private readonly IInterventionDiagnosticService _diagnosticService;
    private readonly IInterventionExecutionService _executionService;
    private readonly InterventionReportBuilder _reportBuilder;
    private readonly List<InterventionDiagnostic> _diagnostics = new();
    private readonly Dictionary<InterventionId, CheckBox> _selectionById = new();
    private CancellationTokenSource? _operationCancellation;
    private TaskCompletionSource<InterventionDecision>? _activeDecision;
    private InterventionSessionReport? _lastReport;
    private bool _operationInProgress;
    private bool _criticalActionStarted;

    public InterventionsView()
        : this(new InterventionDiagnosticService(), new InterventionExecutionService(), new InterventionReportBuilder())
    {
    }

    public InterventionsView(
        IInterventionDiagnosticService diagnosticService,
        IInterventionExecutionService executionService,
        InterventionReportBuilder reportBuilder)
    {
        InitializeComponent();
        _diagnosticService = diagnosticService;
        _executionService = executionService;
        _reportBuilder = reportBuilder;
        RenderDiagnostics();
    }

    public event Action<string>? VirgilMessageRequested;

    public event Action<VirgilCoreState, string>? VirgilStateRequested;

    public event EventHandler? ReturnHomeRequested;

    public void FocusAnalyzeButton()
    {
        AnalyzeInterventionsButton.Focus();
    }

    public void CancelActiveOperation()
    {
        if (_criticalActionStarted)
        {
            VirgilMessageRequested?.Invoke("Action critique deja lancee.\nVirgil attend la fin sans tuer le processus.");
            return;
        }

        _operationCancellation?.Cancel();
        CompleteDecision(InterventionDecision.CancelAll);
    }

    public bool TryCloseOverlay()
    {
        if (InterventionsReportOverlay.Visibility == Visibility.Visible)
        {
            HideReport();
            return true;
        }

        if (InterventionValidationOverlay.Visibility == Visibility.Visible)
        {
            CompleteDecision(InterventionDecision.Skip);
            return true;
        }

        if (_operationInProgress)
        {
            CancelActiveOperation();
            return true;
        }

        return false;
    }

    private async void AnalyzeInterventions_Click(object sender, RoutedEventArgs e)
    {
        await AnalyzeAsync();
    }

    private async void RunGuided_Click(object sender, RoutedEventArgs e)
    {
        await RunGuidedAsync();
    }

    private void ViewInterventionsReport_Click(object sender, RoutedEventArgs e)
    {
        ShowReport();
    }

    private void ReturnHome_Click(object sender, RoutedEventArgs e)
    {
        ReturnHomeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExecuteIntervention_Click(object sender, RoutedEventArgs e)
    {
        CompleteDecision(InterventionDecision.Execute);
    }

    private void SkipIntervention_Click(object sender, RoutedEventArgs e)
    {
        CompleteDecision(InterventionDecision.Skip);
    }

    private void CancelAllInterventions_Click(object sender, RoutedEventArgs e)
    {
        CompleteDecision(InterventionDecision.CancelAll);
    }

    private void CloseInterventionsReport_Click(object sender, RoutedEventArgs e)
    {
        HideReport();
    }

    private async Task AnalyzeAsync()
    {
        if (!BeginOperation("Diagnostic interventions en cours."))
        {
            return;
        }

        try
        {
            RequestVirgilState(VirgilCoreState.Scanning, "INTERVENTIONS");
            VirgilMessageRequested?.Invoke("Diagnostic interventions lance.\nAucune action executee.");

            var diagnostics = await _diagnosticService
                .DiagnoseAllAsync(_operationCancellation!.Token)
                .ConfigureAwait(true);
            _diagnostics.Clear();
            _diagnostics.AddRange(diagnostics);
            RenderDiagnostics();
            RequestVirgilState(VirgilCoreState.Success, "PRET");
            AnnounceDiagnostics();
        }
        catch (OperationCanceledException)
        {
            InterventionsStatusText.Text = "Diagnostic annule.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            VirgilMessageRequested?.Invoke("Diagnostic annule.\nAucune action executee.");
        }
        catch
        {
            InterventionsStatusText.Text = "Diagnostic indisponible.";
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
            VirgilMessageRequested?.Invoke("Diagnostic indisponible.\nAucune action executee.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task RunGuidedAsync()
    {
        var candidates = GuidedCandidates().ToList();
        if (candidates.Count == 0)
        {
            VirgilMessageRequested?.Invoke("Aucune intervention selectionnee.");
            return;
        }

        if (!BeginOperation("Parcours guide en attente de validation."))
        {
            return;
        }

        var startedAt = DateTimeOffset.Now;
        var results = new List<InterventionExecutionResult>();
        var errors = new List<string>();

        try
        {
            foreach (var diagnostic in candidates.OrderBy(diagnostic => diagnostic.Definition.DisplayOrder))
            {
                _operationCancellation!.Token.ThrowIfCancellationRequested();
                var decision = await AskDecisionAsync(diagnostic, _operationCancellation.Token).ConfigureAwait(true);

                if (decision == InterventionDecision.CancelAll)
                {
                    results.Add(_executionService.Cancel(diagnostic));
                    errors.Add("Parcours annule par l'utilisateur.");
                    break;
                }

                if (decision == InterventionDecision.Skip)
                {
                    results.Add(_executionService.Skip(diagnostic));
                    VirgilMessageRequested?.Invoke("Intervention passee.\nAucune action executee.");
                    continue;
                }

                RequestVirgilState(VirgilCoreState.Executing, "EXECUTION");
                _criticalActionStarted = !diagnostic.Definition.CanBeInterruptedAfterStart;
                SetButtonsEnabled(false);

                var result = await _executionService
                    .ExecuteAsync(diagnostic, confirmed: true, _operationCancellation.Token)
                    .ConfigureAwait(true);

                _criticalActionStarted = false;
                results.Add(result);
                VirgilMessageRequested?.Invoke(ResultMessage(result));
            }
        }
        catch (OperationCanceledException)
        {
            errors.Add("Parcours annule.");
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
        }
        catch
        {
            errors.Add("Erreur pendant le parcours guide.");
            RequestVirgilState(VirgilCoreState.Error, "ERREUR");
        }
        finally
        {
            _criticalActionStarted = false;
            _lastReport = _executionService.CreateReport(startedAt, candidates, results, errors);
            EndOperation();
            CompleteSessionState(_lastReport);
            ShowReport();
        }
    }

    private bool BeginOperation(string status)
    {
        if (_operationInProgress)
        {
            VirgilMessageRequested?.Invoke("Operation deja en cours.");
            return false;
        }

        _operationInProgress = true;
        _operationCancellation = new CancellationTokenSource();
        InterventionsStatusText.Text = status;
        SetButtonsEnabled(false);
        Mouse.OverrideCursor = Cursors.Wait;
        return true;
    }

    private void EndOperation()
    {
        _operationInProgress = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        Mouse.OverrideCursor = null;
        SetButtonsEnabled(true);
        UpdateCommandState();
    }

    private void SetButtonsEnabled(bool enabled)
    {
        AnalyzeInterventionsButton.IsEnabled = enabled;
        RunGuidedButton.IsEnabled = enabled && GuidedCandidates().Any();
        ViewInterventionsReportButton.IsEnabled = enabled && _lastReport is not null;
        ReturnHomeButton.IsEnabled = enabled;
    }

    private void UpdateCommandState()
    {
        RunGuidedButton.IsEnabled = !_operationInProgress && GuidedCandidates().Any();
        ViewInterventionsReportButton.IsEnabled = !_operationInProgress && _lastReport is not null;
    }

    private async Task<InterventionDecision> AskDecisionAsync(
        InterventionDiagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        ShowValidation(diagnostic);
        _activeDecision = new TaskCompletionSource<InterventionDecision>();
        using var registration = cancellationToken.Register(() =>
            Dispatcher.BeginInvoke(new Action(() => CompleteDecision(InterventionDecision.CancelAll))));
        return await _activeDecision.Task.ConfigureAwait(true);
    }

    private void ShowValidation(InterventionDiagnostic diagnostic)
    {
        var definition = diagnostic.Definition;
        ValidationTitleText.Text = definition.RiskLevel == InterventionRiskLevel.Sensitive
            ? "ACTION SENSIBLE"
            : "VALIDATION REQUISE";
        ValidationDetailsText.Text = string.Join("\n", new[]
        {
            definition.Title,
            definition.Description,
            $"Effet attendu : {definition.ExpectedEffect}",
            $"Non touche : {definition.NotTouched}",
            $"Risque : {RiskLabel(definition.RiskLevel)}",
            $"Admin : {(definition.RequiresAdministrator ? "oui" : "non")}",
            $"Redemarrage possible : {(definition.RebootPossible ? "oui" : "non")}",
            $"Diagnostic : {diagnostic.StateBefore}",
            $"Recommandation : {diagnostic.Recommendation}"
        });
        ValidationCommandText.Text = FormatCommands(diagnostic);
        ValidationSafetyText.Text = string.Join("\n", diagnostic.Warnings
            .Concat(new[]
            {
                "Validation separee obligatoire.",
                "Aucune commande libre.",
                "Aucun redemarrage automatique.",
                "Aucun Take Ownership dans cette PR."
            }));
        ExecuteInterventionButton.Content = definition.RiskLevel == InterventionRiskLevel.Sensitive
            ? "JE CONFIRME"
            : "EXECUTER";
        InterventionValidationOverlay.Visibility = Visibility.Visible;
        ValidationCore.SetState(definition.RiskLevel == InterventionRiskLevel.Sensitive
            ? VirgilCoreState.SensitiveAction
            : VirgilCoreState.Warning);
        RequestVirgilState(definition.RiskLevel == InterventionRiskLevel.Sensitive
            ? VirgilCoreState.SensitiveAction
            : VirgilCoreState.Warning,
            "VALIDATION");
        ExecuteInterventionButton.Focus();
    }

    private void CompleteDecision(InterventionDecision decision)
    {
        if (_activeDecision is null)
        {
            return;
        }

        ValidationCore.SetState(VirgilCoreState.Idle);
        InterventionValidationOverlay.Visibility = Visibility.Collapsed;
        _activeDecision.TrySetResult(decision);
        _activeDecision = null;
    }

    private void RenderDiagnostics()
    {
        ClearPanels();
        _selectionById.Clear();

        if (_diagnostics.Count == 0)
        {
            AddEmptyCards();
            InterventionsOverallText.Text = "NON ANALYSE";
            RecommendedCountText.Text = "0";
            UpdateCommandState();
            return;
        }

        foreach (var diagnostic in _diagnostics.OrderBy(diagnostic => diagnostic.Definition.DisplayOrder))
        {
            PanelFor(diagnostic.Definition.Category).Children.Add(CreateDiagnosticCard(diagnostic));
        }

        var available = _diagnostics.Count(diagnostic => diagnostic.IsAvailable);
        var recommended = _diagnostics.Count(diagnostic => diagnostic.Status == InterventionStatus.Recommended);
        InterventionsOverallText.Text = $"{available} disponibles. Aucune execution automatique.";
        RecommendedCountText.Text = recommended.ToString();
        UpdateCommandState();
    }

    private UIElement CreateDiagnosticCard(InterventionDiagnostic diagnostic)
    {
        var card = new Border
        {
            Style = TryFindResource("VirgilHudCard") as Style,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var stack = new StackPanel();
        var checkBox = new CheckBox
        {
            Content = diagnostic.Definition.Title,
            IsEnabled = diagnostic.IsAvailable,
            Margin = new Thickness(0, 0, 0, 8)
        };
        checkBox.SetResourceReference(ForegroundProperty, "App.TextPrimaryBrush");
        checkBox.Checked += (_, _) => UpdateCommandState();
        checkBox.Unchecked += (_, _) => UpdateCommandState();
        _selectionById[diagnostic.Definition.Id] = checkBox;

        stack.Children.Add(checkBox);
        stack.Children.Add(CreateText(diagnostic.Definition.Description));
        stack.Children.Add(CreateText($"Risque : {RiskLabel(diagnostic.Definition.RiskLevel)}"));
        stack.Children.Add(CreateText($"Admin : {(diagnostic.Definition.RequiresAdministrator ? "oui" : "non")}"));
        stack.Children.Add(CreateText($"Redemarrage possible : {(diagnostic.Definition.RebootPossible ? "oui" : "non")}"));
        stack.Children.Add(CreateText($"Duree : {diagnostic.Definition.EstimatedDuration}"));
        stack.Children.Add(CreateText($"Statut : {StatusLabel(diagnostic.Status)}"));
        stack.Children.Add(CreateText($"Diagnostic : {diagnostic.StateBefore}"));

        if (diagnostic.Errors.Count > 0)
        {
            stack.Children.Add(CreateText("Erreur : " + string.Join(" | ", diagnostic.Errors.Take(2))));
        }

        card.Child = stack;
        return card;
    }

    private void ClearPanels()
    {
        SystemActionsPanel.Children.Clear();
        NetworkActionsPanel.Children.Clear();
        StorageActionsPanel.Children.Clear();
        InterfaceActionsPanel.Children.Clear();
    }

    private void AddEmptyCards()
    {
        SystemActionsPanel.Children.Add(CreateTextCard("Lancez une analyse pour afficher les actions systeme."));
        NetworkActionsPanel.Children.Add(CreateTextCard("Lancez une analyse pour afficher les actions reseau."));
        StorageActionsPanel.Children.Add(CreateTextCard("Lancez une analyse pour afficher les actions stockage."));
        InterfaceActionsPanel.Children.Add(CreateTextCard("Lancez une analyse pour afficher les actions interface."));
    }

    private StackPanel PanelFor(InterventionCategory category)
    {
        return category switch
        {
            InterventionCategory.Network => NetworkActionsPanel,
            InterventionCategory.Storage => StorageActionsPanel,
            InterventionCategory.Interface => InterfaceActionsPanel,
            _ => SystemActionsPanel
        };
    }

    private UIElement CreateTextCard(string text)
    {
        var card = new Border
        {
            Style = TryFindResource("VirgilHudCard") as Style,
            Margin = new Thickness(0, 0, 0, 8)
        };
        card.Child = CreateText(text);
        return card;
    }

    private TextBlock CreateText(string text)
    {
        return new TextBlock
        {
            Text = text,
            Style = TryFindResource("VirgilHudSecondaryText") as Style,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        };
    }

    private IEnumerable<InterventionDiagnostic> GuidedCandidates()
    {
        var selected = _diagnostics
            .Where(diagnostic => _selectionById.TryGetValue(diagnostic.Definition.Id, out var checkBox) &&
                checkBox.IsChecked == true &&
                diagnostic.IsAvailable)
            .ToList();

        if (selected.Count > 0)
        {
            return selected;
        }

        return _diagnostics.Where(diagnostic =>
            diagnostic.Status == InterventionStatus.Recommended &&
            diagnostic.Definition.RiskLevel != InterventionRiskLevel.Sensitive);
    }

    private void AnnounceDiagnostics()
    {
        var available = _diagnostics.Count(diagnostic => diagnostic.IsAvailable);
        var recommended = _diagnostics.Count(diagnostic => diagnostic.Status == InterventionStatus.Recommended);
        VirgilMessageRequested?.Invoke($"Diagnostic termine.\n{available} actions disponibles.\n{recommended} recommandees.");
    }

    private void CompleteSessionState(InterventionSessionReport report)
    {
        if (report.CancelledActions > 0)
        {
            InterventionsStatusText.Text = "Parcours annule.";
            RequestVirgilState(VirgilCoreState.Idle, "REPOS");
            return;
        }

        if (report.Failures > 0)
        {
            InterventionsStatusText.Text = "Parcours termine avec erreurs.";
            RequestVirgilState(VirgilCoreState.Warning, "ATTENTION");
            return;
        }

        InterventionsStatusText.Text = "Parcours termine.";
        RequestVirgilState(VirgilCoreState.Success, "TERMINE");
    }

    private void ShowReport()
    {
        if (_lastReport is null)
        {
            VirgilMessageRequested?.Invoke("Aucun rapport interventions disponible.");
            return;
        }

        InterventionsReportText.Text = _reportBuilder.Build(_lastReport);
        InterventionsReportOverlay.Visibility = Visibility.Visible;
        CloseInterventionsReportButton.Focus();
    }

    private void HideReport()
    {
        InterventionsReportOverlay.Visibility = Visibility.Collapsed;
        ViewInterventionsReportButton.Focus();
    }

    private void RequestVirgilState(VirgilCoreState state, string label)
    {
        InterventionsMiniCore.SetState(state);
        VirgilStateRequested?.Invoke(state, label);
    }

    private static string ResultMessage(InterventionExecutionResult result)
    {
        return result.Status switch
        {
            InterventionStatus.Completed => "Intervention terminee.\nRapport mis a jour.",
            InterventionStatus.RebootRequired => "Intervention terminee.\nRedemarrage manuel probablement requis.",
            InterventionStatus.PartialFailure => "Intervention partielle.\nRapport disponible.",
            InterventionStatus.Failed => "Intervention en echec.\nRapport disponible.",
            _ => "Intervention traitee.\nRapport disponible."
        };
    }

    private static string FormatCommands(InterventionDiagnostic diagnostic)
    {
        if (diagnostic.Definition.CommandPreviews.Count == 0)
        {
            return "Aucune commande externe.";
        }

        return string.Join("\n", diagnostic.Definition.CommandPreviews.Select(command =>
        {
            var args = command.Arguments.Select(argument => ResolveArgument(argument, diagnostic));
            return command.Executable + " " + string.Join(" ", args);
        }));
    }

    private static string ResolveArgument(string argument, InterventionDiagnostic diagnostic)
    {
        if (argument == "<system-drive>" &&
            diagnostic.TechnicalData.TryGetValue("SystemDrive", out var systemDrive))
        {
            return systemDrive;
        }

        return argument.Contains(' ') ? "\"" + argument + "\"" : argument;
    }

    private static string RiskLabel(InterventionRiskLevel risk)
    {
        return risk switch
        {
            InterventionRiskLevel.Low => "faible",
            InterventionRiskLevel.Moderate => "modere",
            _ => "sensible"
        };
    }

    private static string StatusLabel(InterventionStatus status)
    {
        return status switch
        {
            InterventionStatus.Available => "disponible",
            InterventionStatus.Recommended => "recommande",
            InterventionStatus.Unavailable => "indisponible",
            InterventionStatus.Completed => "terminee",
            InterventionStatus.PartialFailure => "partielle",
            InterventionStatus.Failed => "echec",
            InterventionStatus.Skipped => "passee",
            InterventionStatus.Cancelled => "annulee",
            InterventionStatus.RebootRequired => "redemarrage requis",
            _ => status.ToString()
        };
    }

    private enum InterventionDecision
    {
        Execute,
        Skip,
        CancelAll
    }
}
