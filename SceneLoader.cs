using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public void LoadLegoScene()
    {
        SceneManager.LoadScene("SampleScene"); // Name of your scene
    }
}
