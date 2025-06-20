using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EdgeDetectionEffect : MonoBehaviour
{
    public Shader edgeDetectionShader;
    private Material edgeMaterial;

    void Start()
    {
        // カメラに法線テクスチャを取得させる
        GetComponent<Camera>().depthTextureMode = DepthTextureMode.DepthNormals;

        if (edgeDetectionShader)
        {
            edgeMaterial = new Material(edgeDetectionShader);
        }
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (edgeMaterial)
        {
            Graphics.Blit(source, destination, edgeMaterial);
        }
        else
        {
            Graphics.Blit(source, destination);
        }
    }
}
