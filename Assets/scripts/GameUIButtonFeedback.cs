using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Shared pointer and keyboard focus feedback, independent of game time.</summary>
[RequireComponent(typeof(Button))]
public sealed class GameUIButtonFeedback : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,ISelectHandler,IDeselectHandler
{
    private Button button;
    private Outline outline;
    private bool hovered,selected,primary;
    private float emphasis;
    private Vector3 restScale;
    public void Configure(bool isPrimary)
    {
        primary=isPrimary;button=GetComponent<Button>();
        outline=GetComponent<Outline>()??gameObject.AddComponent<Outline>();
        outline.effectDistance=new Vector2(1,-1);outline.effectColor=GameUIStyle.Border;
    }
    private void Awake(){restScale=transform.localScale;button=GetComponent<Button>();}
    private void OnEnable(){hovered=selected=false;emphasis=0;}
    private void OnDisable(){transform.localScale=restScale;hovered=selected=false;}
    private void Update()
    {
        float target=button!=null&&button.IsInteractable()&&(hovered||selected)?1f:0f;
        emphasis=Mathf.MoveTowards(emphasis,target,Time.unscaledDeltaTime*9f);
        transform.localScale=restScale*(1f+(primary?.012f:.008f)*emphasis);
        if(outline!=null)outline.effectColor=Color.Lerp(GameUIStyle.Border,
            new Color(GameUIStyle.Accent.r,GameUIStyle.Accent.g,GameUIStyle.Accent.b,.85f),emphasis);
    }
    public void OnPointerEnter(PointerEventData eventData)=>hovered=true;
    public void OnPointerExit(PointerEventData eventData)=>hovered=false;
    public void OnSelect(BaseEventData eventData)=>selected=true;
    public void OnDeselect(BaseEventData eventData)=>selected=false;
}
