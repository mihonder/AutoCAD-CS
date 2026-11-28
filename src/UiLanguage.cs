namespace CadDustLab;

public enum UiLanguage { Russian, English }

internal static class GameText
{
    internal static string Pick(this UiLanguage language,string russian,string english)
        =>language==UiLanguage.English?english:russian;

    internal static string Hud(this UiLanguage language,bool captured)=>captured
        ? language.Pick("ИГРА | F6 / Esc — отпустить мышь | WASD | Shift — шаг | Ctrl — присесть | B — купить | H — сложность | G — бросить",
                        "GAME | F6 / Esc — release mouse | WASD | Shift — walk | Ctrl — crouch | B — buy | H — difficulty | G — drop")
        : language.Pick("УПРАВЛЕНИЕ AUTOCAD | F6 — войти в игру | DUSTSTOP — закрыть игру",
                        "AUTOCAD CONTROLS | F6 — enter game | DUSTSTOP — close game");

    internal static string BotName(this UiLanguage language,bool isCt,int id)
        =>$"{(isCt?"CT":"T")} — {language.Pick("Бот","Bot")} {id:00}";
}

// Store both versions of transient messages so a language change also updates
// events already on screen, without resetting their lifetime or the round.
internal readonly record struct UiMessage(string Russian,string English)
{
    internal string In(UiLanguage language)=>language.Pick(Russian,English);
    internal static readonly UiMessage Empty=new("","");
}
