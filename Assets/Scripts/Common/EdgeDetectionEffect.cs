using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EdgeDetectionEffect : MonoBehaviour
{
    public Shader edgeDetectionShader;
    private Material edgeMaterial;

    void Start()
    {
        // �J�����ɖ@���e�N�X�`�����擾������
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
