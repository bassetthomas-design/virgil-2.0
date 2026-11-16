namespace Virgil.App.Speech;

public static class VirgilLines
{
    public static readonly IReadOnlyDictionary<string, string[]> Lists = new Dictionary<string, string[]>
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
}
