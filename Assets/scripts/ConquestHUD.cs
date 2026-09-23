using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Uses the same canvas, safe area and embedded font as the existing player HUD.</summary>
public sealed class ConquestHUD : MonoBehaviour
{
    private RectTransform root, bar, results;
    private Text score, clock, result, nearby;
    private readonly Text[] points = new Text[3], markers = new Text[3];
    private readonly Image[] progress = new Image[3];
    private Camera view;
    private PlayerHealth local;
    private float nextRefresh;
    private static readonly Color Blue = new Color(.25f,.73f,1f), Orange = new Color(1f,.49f,.24f);
    private static Color TeamColor(int team) => team == 1 ? Blue : team == 2 ? Orange : new Color(.75f,.79f,.82f);
    private RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 offset, bool background)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(.5f,1);
        rt.sizeDelta = size; rt.anchoredPosition = offset;
        if (background) { var image = go.AddComponent<Image>(); image.color = new Color(.018f,.027f,.04f,.88f); image.raycastTarget = false; }
        return rt;
    }
    private Text Label(Transform parent, string text, int size, Vector2 dimensions, Vector2 offset)
    {
        var rt = Panel(text, parent, new Vector2(.5f,1), dimensions, offset, false);
        var label = rt.gameObject.AddComponent<Text>(); label.font = GameUIStyle.Font;
        label.text = text; label.fontSize = size; label.color = Color.white;
        label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Overflow;
        return label;
    }
    private void Awake()
    {
        root = GetComponent<PlayerHUD>().ContentRoot as RectTransform;
        bar = Panel("Conquest", root, new Vector2(.5f,1), new Vector2(430,104), new Vector2(0,-12), true);
        score = Label(bar,"",22,new Vector2(420,30),new Vector2(0,-4));
        clock = Label(bar,"",15,new Vector2(420,24),new Vector2(0,-34));
        for(int i=0;i<3;i++)
        {
            float x=(i-1)*125;
            points[i]=Label(bar,"",15,new Vector2(118,28),new Vector2(x,-59));
            var rail=Panel("Capture progress",bar,new Vector2(.5f,1),new Vector2(110,3),new Vector2(x,-94),true);
            var fill=Panel("Fill",rail,new Vector2(0,1),new Vector2(0,3),Vector2.zero,false);
            fill.pivot=new Vector2(0,1); progress[i]=fill.gameObject.AddComponent<Image>(); progress[i].raycastTarget=false;
            markers[i]=Label(root,"",16,new Vector2(130,48),Vector2.zero);
            markers[i].gameObject.AddComponent<Shadow>().effectDistance=new Vector2(1,-1);
        }
        nearby=Label(root,"",18,new Vector2(540,52),Vector2.zero);
        nearby.rectTransform.anchorMin=nearby.rectTransform.anchorMax=new Vector2(.5f,.35f);
        results=Panel("Round results",root,new Vector2(.5f,.62f),new Vector2(600,190),Vector2.zero,true);
        result=Label(results,"",24,new Vector2(570,174),new Vector2(0,-8));
        results.gameObject.SetActive(false);
    }
    private void Update()
    {
        var match=ConquestMatch.Instance;var state=match != null ? match.State : null;
        bool active=PhotonNetwork.InRoom&&state!=null;
        bar.gameObject.SetActive(active);
        if(!active) { results.gameObject.SetActive(false);nearby.text="";foreach(var marker in markers)marker.gameObject.SetActive(false);return; }
        if(view==null||!view.isActiveAndEnabled)
        {
            foreach(var actor in PlayerHealth.ActivePlayers)
                if(actor!=null&&!BotController.IsBot(actor)&&actor.GetComponent<PhotonView>().IsMine)
                { local=actor;view=actor.GetComponentInChildren<Camera>(true);break; }
        }
        bool playing=state.Playing(ConquestMatch.Now);
        for(int i=0;i<3;i++)
        {
            var marker=markers[i]; bool visible=false;
            if(playing&&view!=null&&local!=null&&!local.IsDead)
            {
                Vector3 screen=view.WorldToScreenPoint(match.Sites[i].position+Vector3.up*2);
                visible=screen.z>0&&screen.x>0&&screen.x<Screen.width&&screen.y>0&&screen.y<Screen.height;
                if(visible&&RectTransformUtility.ScreenPointToLocalPointInRectangle(root,screen,null,out var point))
                {
                    marker.rectTransform.anchorMin=marker.rectTransform.anchorMax=root.pivot;
                    marker.rectTransform.anchoredPosition=point;
                    marker.text=match.Sites[i].label+"\n"+Mathf.RoundToInt(Vector3.Distance(local.transform.position,match.Sites[i].position))+GameLocalization.T(" м");
                    marker.color=TeamColor(state.Owner[i]);
                }
            }
            marker.gameObject.SetActive(visible);
        }
        if(Time.unscaledTime<nextRefresh)return;nextRefresh=Time.unscaledTime+.1f;
        int seconds=Mathf.Max(0,Mathf.CeilToInt((float)(state.EndsAt-ConquestMatch.Now)));
        score.text=GameLocalization.Format("<color=#40BAFF>СИНИЕ  {0}</color>     :     <color=#FF7D3D>{1}  ОРАНЖЕВЫЕ</color>", state.BluePoints, state.OrangePoints);
        clock.text=GameLocalization.Format("РАУНД {0}   ·   {1:00}:{2:00}", state.Round, seconds/60, seconds%60);
        nearby.text="";
        for(int i=0;i<3;i++)
        {
            points[i].text=match.Sites[i].label+(state.Contested[i]?GameLocalization.T("  СПОР"):state.Owner[i]==0?"  —":"  ●");
            points[i].color=TeamColor(state.Owner[i]);
            progress[i].rectTransform.sizeDelta=new Vector2(110*Mathf.Abs(state.Control[i]),3);
            progress[i].color=TeamColor(state.Control[i]>0?1:state.Control[i]<0?2:0);
            if(playing&&local!=null&&!local.IsDead&&match.Sites[i].Contains(local.transform.position))
            {
                int team=BotController.TeamOf(local);
                bool neutralizing=team==1?state.Control[i]<0:state.Control[i]>0;
                int percent=Mathf.RoundToInt((neutralizing?1-Mathf.Abs(state.Control[i]):Mathf.Abs(state.Control[i]))*100);
                nearby.text=state.Contested[i]?GameLocalization.T("ТОЧКА ОСПАРИВАЕТСЯ"):state.Owner[i]==team?
                    GameLocalization.T("ТОЧКА ")+match.Sites[i].label+GameLocalization.T(" ПОД КОНТРОЛЕМ  ·  +1 ОЧКО/С"):
                    (neutralizing?GameLocalization.T("НЕЙТРАЛИЗАЦИЯ "):GameLocalization.T("ЗАХВАТ ТОЧКИ "))+match.Sites[i].label+"  ·  "+percent+"%";
            }
        }
        results.gameObject.SetActive(!playing);
        if(!playing)
        {
            string winner=state.Winner==0?GameLocalization.T("НИЧЬЯ"):state.Winner==1?GameLocalization.T("ПОБЕДА СИНЕЙ КОМАНДЫ"):GameLocalization.T("ПОБЕДА ОРАНЖЕВОЙ КОМАНДЫ");
            int wait=Mathf.Max(0,Mathf.CeilToInt((float)(state.EndsAt+ConquestRules.Intermission-ConquestMatch.Now)));
            result.text=GameLocalization.Format("{0}\n{1} : {2}\n\nСледующий раунд через {3} с\nTAB — личные результаты", winner, state.BluePoints, state.OrangePoints, wait);
        }
    }
    private void OnDestroy()
    {
        if(bar!=null)Destroy(bar.gameObject);if(results!=null)Destroy(results.gameObject);if(nearby!=null)Destroy(nearby.gameObject);
        foreach(var marker in markers)if(marker!=null)Destroy(marker.gameObject);
    }
}
