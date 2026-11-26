namespace CadDustLab;

// HUD/menu text is emitted as native CAD text with ISOCPEUR by the CAD adapter.
// Table outlines carry the actual AutoCAD lineweight value 30 = 0.30 mm.
public static class GameDrawing
{
    public static void Draw(List<ScreenPolygon> shapes,CombatSnapshot game,List<ScreenLabel> labels,List<ScreenStroke> strokes,bool draft=false)
    {
        if(game.Paused)game=game with{Menu=-1,Score=false};
        int ink=draft?DraftGeometry.White:0x171918,paper=draft?DraftGeometry.Black:0xf1f0e9;
        void Shape(int color,params double[] xy){var p=new V[xy.Length/2];for(int i=0;i<p.Length;i++)p[i]=new(xy[i*2],xy[i*2+1],0);shapes.Add(new(p,color));}
        void Rect(double x,double y,double w,double h,int color)=>Shape(color,x,y,x+w,y,x+w,y+h,x,y+h);
        void Text(double x,double y,string text,double size=21)=>labels.Add(new(new(x,y,90),text,size,ink));
        void Line(double x,double y,double x2,double y2)=>strokes.Add(new(new[]{new V(x,y,85),new V(x2,y2,85)},Color:ink));
        void Table(double x,double y,double w,double h)
        {
            Rect(x,y,w,h,paper);strokes.Add(new(new[]{new V(x,y,85),new V(x+w,y,85),new V(x+w,y+h,85),new V(x,y+h,85)},true,ink));
        }
        ViewModels.Draw(shapes,game);
        if(game.Scoped&&!game.Dead&&game.Weapon.Shape=="sniper")
        {
            // Opaque outside a circular optic; native white CAD reticle inside.
            const double radius=310;
            for(int i=0;i<96;i++)
            {
                double a=i*Math.PI/48,b=(i+1)*Math.PI/48;
                V Inner(double v)=>new(Math.Cos(v)*radius,Math.Sin(v)*radius,0);
                V Outer(double v){double c=Math.Cos(v),s=Math.Sin(v),r=Math.Min(800/Math.Max(.00001,Math.Abs(c)),450/Math.Max(.00001,Math.Abs(s)));return new(c*r,s*r,0);}
                shapes.Add(new(new[]{Inner(a),Outer(a),Outer(b),Inner(b)},DraftGeometry.Black));
                var p=Inner(a);var q=Inner(b);Line(p.X,p.Y,q.X,q.Y);
            }
            Line(-310,0,310,0);Line(0,-310,0,310);
            foreach(int x in new[]{-120,-60,60,120}){Line(x,-5,x,5);Line(-5,x,5,x);}
        }
        if(game.HitMarker)
        {
            int hit=draft?DraftGeometry.White:0x992923;
            Shape(hit,-16,-13,-13,-16,-4,-7,-7,-4);Shape(hit,16,-13,13,-16,4,-7,7,-4);
            Shape(hit,-16,13,-13,16,-4,7,-7,4);Shape(hit,16,13,13,16,4,7,7,4);
        }
        // Leave room for AutoCAD's floating command line at the bottom of the viewport.
        Table(-770,-390,1540,64);
        foreach(double x in new[]{-505d,-245,200,475})Line(x,-390,x,-326);
        Text(-750,-353,$"Здоровье: {game.Health}");Text(-485,-353,$"Броня: {game.Armor}");
        Text(-225,-351,game.Weapon.Name,19);Text(-225,-377,game.Reload>0?"Перезарядка...":game.Weapon.Shape=="knife"?"Нож":$"Патроны: {game.Ammo} / {game.Reserve}",18);
        Text(220,-353,$"Деньги: {game.Money} $");Text(495,-353,$"Убийства: {game.Kills}");
        if(game.Notice.Length>0&&!game.Score&&!game.Dead){Table(-770,-316,900,38);Text(-755,-304,game.Notice,15);}
        if(game.Rounds&&!game.Score&&game.Menu<0)
        {
            Table(-230,345,460,42);
            int seconds=(int)Math.Ceiling(game.Remaining);Text(-213,359,$"T {game.TWins} : {game.CtWins} CT    РАУНД {game.Round}    {seconds/60:00}:{seconds%60:00}",18);
            if(game.RoundResult.Length>0){Table(-310,235,620,75);Text(-285,278,game.RoundResult,25);Text(-285,250,"Следующий раунд через 5 секунд",19);}
        }
        if(game.Feed.Length>0&&!game.Score&&game.Menu<0)
        {
            double h=game.Feed.Length*28+18;Table(260,350-h,510,h);
            for(int i=0;i<game.Feed.Length;i++)Text(273,326-i*28,game.Feed[i],17);
        }
        if(game.Menu==7&&!game.Score)
        {
            Table(-520,-60,1040,350);Text(-490,245,"ВЫБОР КОМАНДЫ",28);Line(-520,220,520,220);
            Text(-490,165,"1  ТЕРРОРИСТЫ — T"+(game.PlayerTeam==Team.T?"  [ВЫБРАНО]":""),24);
            Text(-490,112,"2  СПЕЦНАЗ — CT"+(game.PlayerTeam==Team.CT?"  [ВЫБРАНО]":""),24);
            Text(-490,48,"Смена команды начнёт новый раунд. Боты выровняют составы 5 на 5.",18);
            Text(-490,-20,"M / Esc — закрыть. F6 — отпустить управление",18);
        }
        if(game.Menu==6&&!game.Score)
        {
            Table(-520,-70,1040,380);Text(-490,270,"СЛОЖНОСТЬ БОТОВ",28);Line(-520,250,520,250);
            for(int i=0;i<Combat.BotSkills.Length;i++)
            {var s=Combat.BotSkills[i];Text(-490,208-i*47,$"{i+1}  {s.Name}"+(i==game.Difficulty?"  [ВЫБРАНО]":""),24);}
            Text(-490,-10,"Меняются реакция, точность, очереди и преследование",18);
            Text(-490,-45,"H / Esc — закрыть. F6 — отпустить управление",18);
        }
        if(game.Menu>=0&&game.Menu<6&&!game.Score)
        {
            var weapons=Combat.Catalog.Where(w=>w.Category==game.Menu).ToArray();
            string[] rows=game.Menu==0?new[]{"1  Пистолеты","2  Дробовики","3  Пистолеты-пулемёты","4  Винтовки","5  Пулемёт","6  Патроны основного оружия","7  Патроны пистолета","8  Бронежилет"}:weapons.Select(w=>$"{w.MenuSlot}  {w.Name}").ToArray();
            string title=game.Menu switch{0=>"ПОКУПКА ОРУЖИЯ",1=>"ПИСТОЛЕТЫ",2=>"ДРОБОВИКИ",3=>"ПИСТОЛЕТЫ-ПУЛЕМЁТЫ",4=>"ВИНТОВКИ",_=>"ПУЛЕМЁТ"};
            double bottom=292-(rows.Length+3)*41;
            Table(-720,bottom,840,360-bottom);Line(-720,305,120,305);
            Text(-699,326,title,26);
            for(int i=0;i<rows.Length;i++)
            {
                double y=276-i*41;Text(-699,y,rows[i],22);Line(-720,y-13,120,y-13);
                if(game.Menu>0)Text(-40,y,game.Owned.Contains(weapons[i].Id)?"Есть":$"{weapons[i].Price} $",20);
                else if(i==7)Text(-40,y,"650 $",20);
                else if(i is 5 or 6)
                {
                    int slot=i-5;var weapon=game.Owned.Length>slot?Combat.Catalog.FirstOrDefault(w=>w.Id==game.Owned[slot]):null;
                    if(weapon is not null)Text(-40,y,$"{weapon.Rule.Ammo.Price} $",20);
                }
            }
            Text(-699,bottom+54,game.Menu==0?"0 / B / Esc  Закрыть":"0  Назад     B / Esc  Закрыть",19);
            Text(-699,bottom+20,$"Баланс: {game.Money} $. Патроны — отдельная покупка.",18);
        }
        if(game.Dead&&!game.Score&&game.Menu<0)
        {
            if(game.Rounds)
            {
                Table(-760,-310,1520,90);
                if(game.DeathCamera)
                {
                    Text(-742,-246,"ВЫ УБИТЫ",21);
                    Text(-742,-276,$"Наблюдение через {game.DeathRemaining:F1} с | Удержать Space 1,2 с — новый раунд",19);
                }
                else
                {
                    string mode=game.SpectatorMode switch{0=>"ЗА БОТОМ",1=>"СВОБОДНЫЙ ПОЛЁТ",_=>"ПЛАН"};
                    Text(-742,-246,$"ВЫ УБИТЫ | {mode} | {game.SpectatorName}",21);
                    Text(-742,-276,"Space — режим | ЛКМ / ПКМ — другой бот | Удержать Space 1,2 с — новый раунд",19);
                }
                if(game.RestartProgress>0)Rect(-742,-297,1480*game.RestartProgress,5,ink);
            }
            else{Table(-360,-60,720,130);Text(-325,22,"ВЫ УБИТЫ",32);Text(-325,-26,"Возвращение через 3 секунды",22);}
        }
        if(game.Score)
        {
            Table(-620,-310,1240,670);Text(-590,321,"DUST 2 — ТАБЛИЦА СЧЁТА",28);
            Text(-590,275,"Игрок",22);Text(60,275,"Убийства",22);Text(260,275,"Смерти",22);Text(435,275,"Состояние",20);Line(-620,261,620,261);
            double y=226;
            foreach(bool enemy in new[]{false,true})
            {
                Text(-590,y,enemy?"СПЕЦНАЗ — CT":"ТЕРРОРИСТЫ — T",23);y-=39;
                foreach(var row in game.Scores.Where(r=>r.IsCt==enemy).OrderByDescending(r=>r.Kills))
                {
                    Text(-570,y,row.Name,20);Text(90,y,row.Kills.ToString(),20);Text(290,y,row.Deaths.ToString(),20);Text(435,y,row.Dead?"Убит":"В игре",20);
                    Line(-620,y-11,620,y-11);y-=32;
                }
                y-=24;
            }
            Text(-590,-285,"Tab — удерживать для просмотра",18);
        }
        // Draw last so preparation remains visible above B/Tab menus. The number
        // pulses twice per second while a continuous bar makes progress obvious.
        if(game.Rounds&&game.Freeze>0&&!game.Paused)
        {
            // Scoreboard header is replaced by the countdown; do not cover rows.
            if(game.Score)
            {
                labels.RemoveAll(x=>x.Text=="DUST 2 — ТАБЛИЦА СЧЁТА");
                Text(-590,321,$"ПОДГОТОВКА РАУНДА — {Math.Ceiling(game.Freeze):0} СЕК. ДО СТАРТА",28);
                Rect(-590,305,1180*Compat.Clamp(game.Freeze/3,0,1),4,ink);
            }
            else
            {
            Table(180,152,590,145);
            Text(198,265,"ПОДГОТОВКА РАУНДА",23);
            int color=(int)(game.Freeze*4)%2==0?ink:(draft?0x707070:0x909090);
            labels.Add(new(new(205,207,90),Math.Ceiling(game.Freeze).ToString("0"),46,color));
            Text(270,230,"СЕК. ДО СТАРТА",20);Text(270,198,"B — ПОКУПКА",20);
            Rect(198,166,552*Compat.Clamp(game.Freeze/3,0,1),5,ink);
            }
        }
        if(game.Paused)
        {
            Table(-340,80,680,125);Text(-315,160,"ПАУЗА — УПРАВЛЕНИЕ AUTOCAD",25);
            Text(-315,117,"F6 — ВОЙТИ В ИГРУ",25);
        }
    }
}
