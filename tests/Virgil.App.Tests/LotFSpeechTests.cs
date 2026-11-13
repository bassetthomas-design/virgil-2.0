using System.Collections;
using System.Reflection;
using Virgil.App.ViewModels;
using Xunit;

namespace Virgil.Tests;

public sealed class LotFSpeechTests
{
    internal static readonly Dictionary<string, string[]> Expected = new()
    {
        ["Welcome"] = ["Bonjour. Je surveille votre PC, puisque personne d'autre ne le fait.", "Me revoilà. Lancez un scan quand vous voulez, je ne suis pas pressé.", "Virgil en service. Je ne juge pas vos onglets ouverts. Pas à voix haute."],
        ["RunningHumour"] = ["Je m'en occupe. Regarder la barre ne la fera pas avancer plus vite.", "Au travail. Prévenez Windows qu'on va lui demander des choses.", "C'est parti. Je vous préviens si quelque chose mérite votre attention."],
        ["RunningSober"] = ["Je m'en occupe. Le détail s'affiche au fur et à mesure.", "En cours. Vous pouvez demander l'annulation avec le bouton Annuler.", "Au travail. Chaque étape apparaît ci-dessous avec son résultat."],
        ["SucceededHumour"] = ["Voilà. Pour une fois, personne n'a protesté.", "Fait proprement. Je ne m'attends pas à des remerciements.", "Rien n'a résisté. C'est presque décevant."],
        ["SucceededSober"] = ["C'est fait. Le bilan ci-dessus donne le détail.", "Opération menée à bien. Le détail est juste au-dessus.", "Fait. Le bilan ci-dessus dit ce qui a changé."],
        ["NeedsReview"] = ["Presque. Il reste des points à regarder dans le bilan ci-dessus.", "Pas tout à fait fini : le bilan dit ce qui reste et quoi faire.", "Windows a gardé quelques surprises. Le détail est juste au-dessus."],
        ["Failed"] = ["Ça n'a pas fonctionné. Le bilan ci-dessus indique ce qui a échoué.", "Un problème est survenu. Lisez le détail avant de relancer.", "Je n'ai pas pu aller au bout. Le détail vous dit pourquoi."],
        ["CancelledHumour"] = ["Annulé. Je range mes outils, sans rancune.", "Arrêt demandé. On reprendra quand vous voudrez.", "Changement de programme, noté. Le bilan montre où j'en étais."],
        ["CancelledSober"] = ["Annulé. Le bilan montre ce qui a été fait avant l'arrêt.", "Arrêt demandé. Rien de nouveau ne sera lancé.", "Annulation prise en compte. Le détail est ci-dessus."],
        ["NeutralHumour"] = ["Le bilan est juste au-dessus. Je l'ai écrit, autant le lire.", "Voilà le constat. Je n'invente rien, je rapporte.", "Le détail ci-dessus dit tout, même ce qui fâche."],
        ["NeutralSober"] = ["Voici ce que j'ai constaté. À vous de juger.", "Le détail est ci-dessus, sans enjolivure.", "Le bilan ci-dessus résume ce qui s'est passé."],
    };
    internal static Type Type(string name)
    {
        var type = typeof(LongOperationViewModel).Assembly.GetType("Virgil.App." + name);
        Assert.NotNull(type); return type;
    }
    internal static object Read(object owner, string name)
    {
        var property = owner.GetType().GetProperty(name); Assert.NotNull(property);
        var value = property.GetValue(owner); Assert.NotNull(value); return value;
    }
    internal static object Call(object owner, string name, params object[] args)
    {
        var method = owner.GetType().GetMethod(name); Assert.NotNull(method);
        return method.Invoke(owner, args)!;
    }
    internal static void Set(object owner, string name, object value)
    {
        var property = owner.GetType().GetProperty(name); Assert.NotNull(property); property.SetValue(owner, value);
    }
    [Fact]
    public void Catalogue_is_exact_complete_short_and_contains_no_forbidden_claim()
    {
        var field = Type("Speech.VirgilLines").GetField("Lists"); Assert.NotNull(field);
        var lists = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string[]>>(field.GetValue(null));
        Assert.Equal(Expected.Keys.Order(), lists.Keys.Order());
        foreach (var (key, lines) in Expected)
        {
            Assert.Equal(lines, lists[key]); Assert.True(lines.Length >= 3);
            foreach (var line in lists[key])
            {
                Assert.True(line.Length <= 90);
                foreach (var word in new[] { "à jour", "terminé", "réussi", "tout est", "plus rien à faire", "aucun problème", "tout va bien", "Écoulé", "WinGet", "HUD", "Idle", "state", " COM", "commande" })
                    Assert.DoesNotContain(word, line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
    [Fact]
    public void Fixed_local_day_and_per_owner_per_list_counters_determine_four_draws()
    {
        var clock = new Clock();
        object Picker() => Activator.CreateInstance(Type("Speech.VirgilLinePicker"), clock)!;
        var first = Picker(); var second = Picker();
        for (var n = 0; n < 4; n++)
        {
            var expected = Expected["RunningHumour"][(clock.GetLocalNow().DayOfYear + n) % 3];
            Assert.Equal(expected, Call(first, "Pick", "RunningHumour"));
            Assert.Equal(expected, Call(second, "Pick", "RunningHumour"));
            Call(first, "Pick", "NeutralSober");
        }
    }
    [Theory]
    [InlineData(OperationFinalState.Neutral)]
    [InlineData(OperationFinalState.Succeeded)]
    [InlineData(OperationFinalState.NeedsReview)]
    [InlineData(OperationFinalState.Failed)]
    [InlineData(OperationFinalState.Cancelled)]
    public void Typed_final_state_alone_selects_the_correct_list(OperationFinalState state)
    {
        foreach (var humour in new[] { true, false })
        {
            var clock = new Clock(); var vm = new LongOperationViewModel(() => { }, clock);
            Set(vm, "HumourAllowed", humour);
            vm.Start("Lecture", immediately: true);
            var running = "Running" + (humour ? "Humour" : "Sober");
            Assert.Equal(Expected[running][clock.GetLocalNow().DayOfYear % 3], Read(vm, "Remark"));
            vm.Commentary = "Commentaire inchangé";
            vm.Complete("réussi, tout est à jour, terminé : texte trompeur", finalState: state);
            var key = state.ToString() + (state is OperationFinalState.NeedsReview or OperationFinalState.Failed ? "" : humour ? "Humour" : "Sober");
            Assert.Equal(Expected[key][clock.GetLocalNow().DayOfYear % 3], Read(vm, "Remark"));
            Assert.Equal("Commentaire inchangé", vm.Commentary);
        }
    }
    [Fact]
    public void Cancellation_wins_over_requested_success()
    {
        var vm = new LongOperationViewModel(() => { }, new Clock()); vm.Start("Lecture");
        vm.AcknowledgeCancellation(); vm.Complete("Bilan", finalState: OperationFinalState.Succeeded);
        Assert.Equal(OperationFinalState.Cancelled, vm.FinalState);
        Assert.Contains((string)Read(vm, "Remark"), Expected["CancelledHumour"]);
    }
    [Fact]
    public void Speech_has_no_external_effects_and_all_six_panels_share_the_binding()
    {
        var root = TestRepositoryRoot.FindRepositoryRoot();
        var directory = Path.Combine(root, "src/Virgil.App/Speech");
        Assert.True(Directory.Exists(directory));
        foreach (var path in Directory.GetFiles(directory, "*.cs"))
        foreach (var forbidden in new[] { "Random", "HttpClient", "System.Net", "Process", "File.", "DateTime.Now", "Thread.Sleep", ".Wait()", ".Result" })
            Assert.DoesNotContain(forbidden, File.ReadAllText(path));
        foreach (var name in new[] { "Cleanup", "Applications", "Interventions" })
            Assert.Contains("Operation.HumourAllowed = false", File.ReadAllText(Path.Combine(root, "src/Virgil.App/ViewModels/" + name + "ViewModel.cs")));
        var panel = File.ReadAllText(Path.Combine(root, "src/Virgil.App/Controls/OperationProgressPanel.xaml"));
        Assert.Contains("VirgilRemarkText", panel); Assert.Contains("Binding Remark", panel);
        Assert.True(panel.IndexOf("Binding Remark", StringComparison.Ordinal) > panel.IndexOf("Binding Commentary", StringComparison.Ordinal));
        Assert.DoesNotContain("<TextBox", File.ReadAllText(Path.Combine(root, "src/Virgil.App/MainWindow.xaml")));
    }
    internal sealed class Clock : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(2), "Test", "Test");
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 12, 23, 30, 0, TimeSpan.Zero);
    }
}
