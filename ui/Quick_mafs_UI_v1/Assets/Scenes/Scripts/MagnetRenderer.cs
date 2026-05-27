using System.Collections.Generic;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using UnityEngine.UI;

public class MagnetCoords
{
    public float x;
    public float y;
}

public class InfoResponse
{
    public Dictionary<string, MagnetCoords> magnets;
}

public class MagnetRenderer : MonoBehaviour
{
    [SerializeField] private RawImage miniDisplay;
    [SerializeField] private float pollIntervalSeconds = 0.5f;
    [SerializeField] private int magnetRadius = 4;

    private const int W = 160;
    private const int H = 120;

    private Texture2D tex;
    private Color32[] pixels;
    private readonly Color32 bg = new Color32(30, 30, 40, 255);

    private Color32[] palette =
    {
        new Color32(  0,   0,   0, 255),
        new Color32(230,  60,  60, 255),
        new Color32( 60, 200,  90, 255),
        new Color32( 70, 130, 230, 255),
    };

}


