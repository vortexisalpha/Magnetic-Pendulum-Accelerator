using UnityEngine;
using UnityEngine.UI;

//drives fss display mode from a ui toggle. flipping it flags the board (via the
//existing PARAMS frame) to render the final-state-sensitivity map and tells the
//renderer to colour incoming frames as fss words. no new tcp events are added.
public class FssModeToggle : MonoBehaviour
{
    [Tooltip("Toggle that enables FSS mode. If left empty, a Toggle on this object is used.")]
    [SerializeField] private Toggle toggle;

    void Awake()
    {
        if (toggle == null)
            toggle = GetComponent<Toggle>();
    }

    void Start()
    {
        if (toggle != null)
        {
            toggle.onValueChanged.AddListener(SetFss);
            SetFss(toggle.isOn); //sync board/renderer with the initial toggle state
        }
    }

    void OnDestroy()
    {
        if (toggle != null)
            toggle.onValueChanged.RemoveListener(SetFss);
    }

    //wire this to a Toggle's OnValueChanged in the inspector if not using `toggle`
    public void SetFss(bool on)
    {
        PynqConnection.Instance?.SetFssMode(on);
        PynqParamController.NotifyFssChanged();
    }
}
