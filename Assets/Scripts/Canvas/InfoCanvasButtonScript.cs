using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static KssBaseScript;

public class InfoCanvasButtonScript : BaseBehaviour
{

    [SerializeField]
    private  Sprite open;
    [SerializeField]
    private Sprite close;

    private Image img;
    private RectTransform rect;
    private TextMeshProUGUI meshUnit;
    private TextMeshProUGUI meshInfo;

    public bool isOpen = false;
    private bool isShowLabel = false;

    private Dictionary<string, CanvasValue> values = new Dictionary<string, CanvasValue>();

    private string unitName;

    protected override void Start()
    {
        base.Start();

        // èâä˙ê›íË
        img = transform.GetComponentInChildren<Image>();
        rect = transform.parent.GetComponent<RectTransform>();
        meshUnit = transform.GetComponentsInChildren<TextMeshProUGUI>()[0];
        meshInfo = transform.GetComponentsInChildren<TextMeshProUGUI>()[1];
        RenewVisp();
    }

    public void OnPointerEnter()
    {
        isShowLabel = true;
        RenewVisp();
    }

    public void OnPointerExit()
    {
        isShowLabel = false;
        RenewVisp();
    }

    // Update is called once per frame
    public void OnPressed()
    {
        if ((open != null) && (close != null))
        {
            if (img.sprite.name == open.name)
            {
                img.sprite = close;
            }
            else
            {
                img.sprite = open;
            }
            RenewVisp();
        }
    }

    private void RenewVisp()
    {
        if ((open != null) && (close != null))
        {
            if (img.sprite.name == open.name)
            {
                rect.sizeDelta = new Vector2
                {
                    x = 0.1f,
                    y = 0.1f
                };
                isOpen = false;
            }
            else
            {
                rect.sizeDelta = new Vector2
                {
                    x = 1,
                    y = (values.Count + 2) * 0.1f
                };
                isOpen = true;
            }
        }
        else
        {
            isOpen = false;
        }
        meshUnit.text = unitName;
        meshUnit.gameObject.SetActive(isShowLabel || isOpen);
        meshInfo.gameObject.SetActive(isOpen);
        if (isOpen)
        {
            var text = "";
            foreach (var value in values)
            {
                text += value.Key +  " : " + value.Value.disp + "\n";
            }
            meshInfo.text = text;
        }
    }

    public void SetValues(string name, Dictionary<string, CanvasValue> values)
    {
        unitName = name;
        this.values = values;
        RenewVisp();
    }
}
