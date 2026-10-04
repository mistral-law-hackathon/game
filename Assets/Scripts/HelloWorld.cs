using UnityEngine;

public class HelloWorld : MonoBehaviour
{
    GUIStyle style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<HelloWorld>() == null)
            new GameObject("HelloWorld").AddComponent<HelloWorld>();
    }

    void OnGUI()
    {
        style ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        style.fontSize = Mathf.RoundToInt(Screen.height * 0.08f);
        GUI.Label(new Rect(0, 0, Screen.width, Screen.height), "Hello, World!", style);
    }
}
