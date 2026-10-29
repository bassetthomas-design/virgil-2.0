using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using static Virgil.Tests.TestRepositoryRoot;

namespace Virgil.Tests;

public sealed class InterfaceAccentTests
{
    private sealed record Label(string File, int Line, string Text);
    private static string AccentFile(string name) => Path.Combine(FindRepositoryRoot(), "tests", "Virgil.App.Tests", "Accents", name);
    private static Dictionary<string, string> ReadPairs(string name) => File.ReadLines(AccentFile(name))
        .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
        .Select(line => line.Split('=', 2)).ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

    // File + exact literal only. These are matching terms, not user-facing labels.
    private static readonly (string File, string Text, string Reason)[] Exemptions =
    [
        ("Virgil.ElevatedHelper/RepairScripts.cs", """
        $ErrorActionPreference='Stop'
        function Check($key,$ok,$detail) { if($ok){"VIRGIL|$key|OK|$detail"}else{"VIRGIL|$key|FINDING|$detail"} }
        foreach($name in @('wuauserv','AppXSvc','ClipSVC','Audiosrv','AudioEndpointBuilder','Spooler')) {
          try { $s=Get-Service -Name $name
            if($name -in @('wuauserv','AppXSvc','ClipSVC')) {
              if($s.StartType -eq 'Disabled'){ Check $name $false "$name désactivé : vérifiez la configuration dans Windows" }
              else { "VIRGIL|$name|INFO|$($s.Status) (démarrage à la demande possible)" }
            }
            else { Check $name ($s.Status -eq 'Running') "$name : $($s.Status)" }
          } catch { "VIRGIL|$name|UNKNOWN|État du service indisponible" }
        }
        $pending=(Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
          (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
        Check 'Reboot' (!$pending) 'Redémarrage Windows en attente'
        try {
          $net=Get-NetIPConfiguration | Where-Object {$_.IPv4DefaultGateway} | Select-Object -First 1
          if(!$net){ Check 'Gateway' $false 'Aucune passerelle active' }
          else { Check 'Gateway' (Test-Connection -ComputerName $net.IPv4DefaultGateway.NextHop -Count 1 -Quiet) 'Réponse de la passerelle' }
          $dns=Resolve-DnsName 'www.microsoft.com' -DnsOnly -ErrorAction Stop
          Check 'DNS' ($null -ne $dns) 'Résolution DNS de www.microsoft.com'
        } catch { Check 'DNS' $false 'Passerelle ou DNS injoignable ; un filtrage réseau est possible' }
        try {
          $queues=Get-Printer -ErrorAction Stop
          $stuck=@(foreach($p in $queues){Get-PrintJob -PrinterName $p.Name -ErrorAction Stop | Where-Object { $_.JobStatus -match 'Error|Blocked|Offline|PaperOut|UserIntervention' -or $_.SubmittedTime -lt (Get-Date).AddHours(-1) }})
          Check 'PrintQueue' ($stuck.Count -eq 0) "$($stuck.Count) tâche(s) d’impression bloquée(s)"
        } catch { 'VIRGIL|PrintQueue|UNKNOWN|File d’impression indisponible' }
        try {
          $endpoints=@(Get-PnpDevice -Class AudioEndpoint -PresentOnly -ErrorAction Stop | Where-Object Status -eq 'OK')
          Check 'AudioEndpoint' ($endpoints.Count -gt 0) "$($endpoints.Count) point(s) de sortie audio disponible(s) ; sortie par défaut à vérifier dans Windows"
        } catch { 'VIRGIL|AudioEndpoint|UNKNOWN|Points de sortie audio indisponibles' }
        """, "Exact PowerShell Services command: $detail is a technical variable; displayed messages already accented. Any script change invalidates this exemption."),
        ("Virgil.Core/Cleanup/CleanupSelectionAction.cs", "Selection:", "ConfirmedPlan parameter key; never a label, no renaming permitted."),
        ("Virgil.Core/Cleanup/CleanupSafetyClassifier.cs", "video", "Personal path keyword, deliberately accepts unaccented names."),
        ("Virgil.Core/Cleanup/CleanupSafetyClassifier.cs", "bebe", "Personal path keyword, deliberately accepts unaccented names."),
        ("Virgil.Core/Applications/ApplicationRemnantClassifier.cs", "videos", "Personal path keyword."),
        ("Virgil.Core/Applications/ApplicationRemnantClassifier.cs", "bibliotheque", "Personal path keyword."),
        ("Virgil.Core/Applications/ApplicationRiskClassifier.cs", "securite", "Protection matching keyword, never displayed."),
        ("Virgil.Core/Applications/ApplicationRiskClassifier.cs", "premiere", "Adobe Premiere product matching keyword."),
        ("Virgil.Core/Reports/ReportSanitizer.cs", "Telechargements", "Stable path replacement token in existing reports."),
        ("Virgil.Core/Updates/UpdateSequenceService.cs", "updates.sequence", "ConfirmedPlan protocol key, also covered by technical token rule.")
    ];

    private static bool IsTechnicalToken(string text) => !text.Any(char.IsWhiteSpace) && text.IndexOfAny(['.', '-', '_', '\\', '/']) >= 0;
    private static bool IsExempt(Label label) => IsTechnicalToken(label.Text) ||
        Exemptions.Any(item => item.File == label.File && item.Text == label.Text);

    private static IEnumerable<Label> CSharpLabels(string file, string code)
    {
        // Roslyn excludes comments, identifiers and interpolation expressions. ValueText decodes escapes.
        var tree = CSharpSyntaxTree.ParseText(code);
        foreach (var token in tree.GetRoot().DescendantTokens())
            if (token.IsKind(SyntaxKind.StringLiteralToken) || token.IsKind(SyntaxKind.Utf8StringLiteralToken) ||
                token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken) || token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken) ||
                token.IsKind(SyntaxKind.Utf8SingleLineRawStringLiteralToken) || token.IsKind(SyntaxKind.Utf8MultiLineRawStringLiteralToken) ||
                token.IsKind(SyntaxKind.InterpolatedStringTextToken))
                yield return new(file, tree.GetLineSpan(token.Span).StartLinePosition.Line + 1, token.ValueText);
    }

    private static IEnumerable<Label> XmlLabels(string file, string code)
    {
        var document = XDocument.Parse(code, LoadOptions.SetLineInfo);
        foreach (var element in document.Descendants())
        {
            var values = element.Attributes().Where(attribute => attribute.Name.NamespaceName.Length == 0 &&
                attribute.Name.LocalName is "Text" or "Content" or "Header" or "ToolTip" or "Title" or "AutomationProperties.Name" or "AutomationProperties.HelpText")
                .Select(attribute => (Value: attribute.Value, Node: (XObject)attribute))
                .Concat(element.Nodes().OfType<XText>().Select(text => (Value: text.Value, Node: (XObject)text)));
            foreach (var value in values)
                if (!string.IsNullOrWhiteSpace(value.Value) && !value.Value.TrimStart().StartsWith('{'))
                    yield return new(file, ((IXmlLineInfo)value.Node).LineNumber, value.Value);
        }
    }

    private static Label[] SourceLabels()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        return Directory.EnumerateDirectories(sourceRoot, "Virgil.*")
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
            .Where(path => Path.GetExtension(path) is ".cs" or ".xaml" or ".resx")
            .SelectMany(path =>
            {
                var file = Path.GetRelativePath(sourceRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                var code = File.ReadAllText(path);
                return Path.GetExtension(path) == ".cs" ? CSharpLabels(file, code) : XmlLabels(file, code);
            }).Where(label => !IsExempt(label)).ToArray();
    }

    private static string StripAccents(string word) => string.Concat(word.Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)).Normalize(NormalizationForm.FormC);
    private static IEnumerable<string> Words(string text) => Regex.Matches(text, @"[\p{L}\p{M}]+").Select(match => match.Value);

    private static Dictionary<string, string> BuildDictionary(IEnumerable<string> corpus, Dictionary<string, string> dictionary, Dictionary<string, string> valid)
    {
        var result = new Dictionary<string, string>(dictionary, StringComparer.OrdinalIgnoreCase);
        foreach (var word in corpus.SelectMany(Words))
        {
            var plain = StripAccents(word);
            if (!plain.Equals(word, StringComparison.OrdinalIgnoreCase) && !valid.ContainsKey(plain))
                result.TryAdd(plain, word);
        }
        return result;
    }

    private static IEnumerable<(string Wrong, string Correct)> Violations(string text, Dictionary<string, string> dictionary)
    {
        if (IsTechnicalToken(text)) yield break;
        foreach (var word in Words(text))
            // Unicode letters/marks are collected whole: no substring match inside défaut/detections.
            if (dictionary.TryGetValue(word, out var correct)) yield return (word, correct);
    }

    [Fact]
    public void Interface_labels_preserve_french_accents_in_all_source_projects()
    {
        var labels = SourceLabels();
        var dictionary = BuildDictionary(labels.Select(label => label.Text), ReadPairs("MotsAccentues.txt"), ReadPairs("ValideSansAccent.txt"));
        var failures = labels.SelectMany(label => Violations(label.Text, dictionary)
            .Select(word => $"{label.File}:{label.Line} : « {word.Wrong} » → « {word.Correct} » : \"{label.Text}\"")).ToArray();
        Assert.True(failures.Length == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Dictionary_has_at_least_250_unambiguous_entries_and_no_whitelist_overlap()
    {
        var dictionary = ReadPairs("MotsAccentues.txt");
        var valid = ReadPairs("ValideSansAccent.txt");
        Assert.True(dictionary.Count >= 250);
        Assert.All(dictionary, pair => { Assert.NotEqual(pair.Key, pair.Value); Assert.Equal(pair.Key, StripAccents(pair.Value)); });
        Assert.Empty(dictionary.Keys.Intersect(valid.Keys, StringComparer.OrdinalIgnoreCase));
        Assert.All(valid.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
    }

    // Known limits: a/à, ou/où and homographic verbs/masculine participles
    // (annule, conserve, termine, initialise, etc.) require contextual review.
    [Theory]
    [InlineData("RAM elevee", true)]
    [InlineData("RAM élevée", false)]
    [InlineData("DETECTE", true)]
    [InlineData("Detecte", true)]
    [InlineData("Analyse terminée", false)]
    [InlineData("Rapport genere", true)]
    [InlineData("cleanup.selection", false)]
    [InlineData("Ou se trouve", false)]
    [InlineData("approfondi approfondie interrompu interrompue défaut", false)]
    public void Dictionary_and_self_feeding_detect_words_with_unicode_boundaries(string text, bool rejected)
    {
        var dictionary = ReadPairs("MotsAccentues.txt");
        dictionary.Remove("genere"); // Must be learned from the corpus, never from the fixed list.
        var effective = BuildDictionary(["Rapport généré"], dictionary, ReadPairs("ValideSansAccent.txt"));
        Assert.Equal(rejected, Violations(text, effective).Any());
    }

    [Theory]
    [InlineData("\"RAM elevee\"")]
    [InlineData("$\"RAM elevee {count}\"")]
    [InlineData("@\"RAM elevee\"")]
    [InlineData("\"\"\"RAM elevee\"\"\"")]
    [InlineData("$\"\"\"RAM elevee {count}\"\"\"")]
    public void CSharp_string_forms_are_scanned(string expression) =>
        Assert.Contains(CSharpLabels("test.cs", "var text = " + expression + ";"), label => label.Text.Contains("RAM elevee", StringComparison.Ordinal));

    [Theory]
    [InlineData("detection", true)]
    [InlineData("detections", false)]
    [InlineData("détection", false)]
    [InlineData("défaut", false)]
    [InlineData("prédetection", false)]
    public void Unicode_words_are_never_split_or_matched_as_substrings(string text, bool rejected) =>
        Assert.Equal(rejected, Violations(text, new(StringComparer.OrdinalIgnoreCase) { ["detection"] = "détection" }).Any());

    [Theory]
    [InlineData("annule")]
    [InlineData("conserve")]
    [InlineData("termine")]
    [InlineData("initialise")]
    public void Whitelist_wins_for_ambiguous_masculine_participles(string word)
    {
        var dictionary = BuildDictionary([word[..^1] + "é"], ReadPairs("MotsAccentues.txt"), ReadPairs("ValideSansAccent.txt"));
        Assert.DoesNotContain(word, dictionary.Keys);
    }

    // These contextual labels were already accented by L5-2 on the required base.
    // Keep explicit guards because the general whitelist deliberately accepts their verbs.
    [Theory]
    [InlineData("Virgil.App/MainWindow.xaml.cs", "Protocole d'analyse initialisé.")]
    [InlineData("Virgil.Core/Scanning/SystemScanService.cs", "Scan terminé.")]
    [InlineData("Virgil.App/ViewModels/InterventionsViewModel.cs", "Diagnostic annulé.")]
    [InlineData("Virgil.App/ViewModels/UpdatesViewModel.cs", "Rapport conservé en mémoire.")]
    [InlineData("Virgil.App/Views/ResourcesView.xaml", "Aucun redémarrage forcé.")]
    [InlineData("Virgil.App/ViewModels/ResourcesViewModel.cs", "Aucun redémarrage forcé.")]
    [InlineData("Virgil.App/Views/ApplicationsView.xaml", "Aucun dossier d'application supprimé directement.")]
    [InlineData("Virgil.App/ViewModels/ApplicationsViewModel.cs", "vous avez sauvegardé ce qui doit l'être.")]
    [InlineData("Virgil.Core/Scanning/SystemScanService.cs", "Aucun processus n'a été modifié.")]
    [InlineData("Virgil.Core/Applications/ApplicationRemnantClassifier.cs", "Reste AppData non classé : lecture seule et revue manuelle.")]
    [InlineData("Virgil.Core/Cleanup/CleanupZoneCatalog.cs", "Aucun nettoyage n'est proposé.")]
    [InlineData("Virgil.Core/Resources/ProcessProtectionPolicy.cs", "Processus installé dans Windows protégé.")]
    [InlineData("Virgil.Core/Interventions/ExplorerRestarter.cs", "Relance Explorer annulée avant exécution complète.")]
    [InlineData("Virgil.App/ViewModels/CleanupViewModel.cs", "NETTOYAGE SÛR - VALIDATION REQUISE")]
    [InlineData("Virgil.App/ViewModels/CleanupViewModel.cs", "NETTOYAGE SÛR")]
    [InlineData("Virgil.Core/Reports/ReportMapper.cs", "Nettoyage sûr : ")]
    [InlineData("Virgil.Core/Scanning/SystemScanService.cs", "Un nettoyage pourra être envisagé après validation explicite.")]
    [InlineData("Virgil.Core/Resources/ResourceRecommendationService.cs", "CPU à surveiller : aucun pic isolé ne justifie une action automatique.")]
    [InlineData("Virgil.Core/Cleanup/CleanupZoneCatalog.cs", "Fichiers d'installation temporaires clairement identifiés.")]
    [InlineData("Virgil.Core/Cleanup/CleanupZoneCatalog.cs", "Fichiers placés volontairement dans la corbeille.")]
    [InlineData("Virgil.Core/Reports/ReportExportService.cs", "Masqués dans cet export.")]
    [InlineData("Virgil.App/ViewModels/ReportsHistoryViewModel.cs", "Rapport exporté manuellement en TXT.")]
    [InlineData("Virgil.App/Hosting/GlobalExceptionHandler.cs", "suite à une erreur inattendue.")]
    [InlineData("Virgil.App/MainWindow.xaml.cs", " à revoir (")]
    [InlineData("Virgil.App/ViewModels/ResourcesViewModel.cs", "À utiliser seulement si")]
    [InlineData("Virgil.App/ViewModels/ResourcesViewModel.cs", "Aucun processus accessible à afficher.")]
    [InlineData("Virgil.App/ViewModels/ResourcesViewModel.cs", "À SURVEILLER")]
    [InlineData("Virgil.Contracts/ScanRules.cs", "À surveiller")]
    [InlineData("Virgil.Core/Interventions/InterventionCatalog.cs", "Une à deux minutes")]
    [InlineData("Virgil.Core/Reports/ReportMapper.cs", "Éléments personnels à revoir : ")]
    [InlineData("Virgil.Core/Reports/ReportMapper.cs", "À surveiller")]
    [InlineData("Virgil.Core/Resources/ResourceRecommendationService.cs", "RAM à surveiller :")]
    [InlineData("Virgil.Core/Scanning/SystemScanService.cs", "Applications à classification incertaine")]
    [InlineData("Virgil.Core/Scanning/SystemScanService.cs", "correspond à une application attendue.")]
    [InlineData("Virgil.Core/Scanning/SystemScanService.cs", "touchent à des composants sensibles.")]
    [InlineData("Virgil.Core/Updates/UpdateRiskClassifier.cs", "Application utilisateur courante à faible risque.")]
    [InlineData("Virgil.Core/Applications/ApplicationUninstallCommandValidator.cs", "Exécutable relatif ou introuvable.")]
    [InlineData("Virgil.Core/Applications/ApplicationUninstallCommandValidator.cs", "Exécutable non reconnu comme désinstalleur officiel.")]
    [InlineData("Virgil.Core/Applications/ApplicationUninstallService.cs", "Exécutable officiel absent après validation.")]
    [InlineData("Virgil.Core/Updates/WingetAvailabilityService.cs", "WinGet non exécutable : ")]
    [InlineData("Virgil.Core/Reports/ReportMapper.cs", "Disques analysés : ")]
    [InlineData("Virgil.Core/Updates/WindowsUpdateStatusService.cs", "Windows Update non confirmé localement.")]
    public void Contextual_masculine_participles_remain_accented(string file, string expected) =>
        Assert.Contains(SourceLabels(), label => label.File == file && label.Text.Contains(expected, StringComparison.Ordinal));

    [Fact]
    public void Official_executable_absence_is_accented_in_both_result_and_errors() =>
        Assert.Equal(2, SourceLabels().Count(label =>
            label.File == "Virgil.Core/Applications/ApplicationUninstallService.cs" &&
            label.Text == "Exécutable officiel absent après validation."));

    [Theory]
    [InlineData("<TextBlock>systeme</TextBlock>")]
    [InlineData("<Button AutomationProperties.Name=\"systeme\" />")]
    [InlineData("<data><value>systeme</value></data>")]
    public void Xml_labels_are_scanned(string xml) => Assert.Contains(XmlLabels("test.xaml", xml), label => label.Text == "systeme");

    [Theory]
    [InlineData("class Detecte { string Selection => nameof(Detecte); // systeme\n}")]
    [InlineData("// RAM elevee")]
    public void CSharp_identifiers_and_comments_are_not_labels(string code) => Assert.Empty(CSharpLabels("test.cs", code));

    [Fact]
    public void Xaml_references_are_not_labels() => Assert.Empty(XmlLabels("test.xaml",
        "<Button xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Name=\"Selection\" Content=\"{Binding Details}\" AutomationProperties.AutomationId=\"Detecte\" />"));
}
