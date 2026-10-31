using System.Collections.Generic;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.ProgressSync;
using Gameplay.UI.Others.UIGameLogic;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.Multiplayer.Client.Notifications;

public class NotificationManager
{
	private const int MAX_LINES = 4;

	private const int LINE_HEIGHT = 20;

	private RectTransform messageBox;

	private Text[] textLines;

	private readonly List<NotificationLine> currentMessages = new List<NotificationLine>();

	public void DisplayNotification(string notification)
	{
		ModLog.Info("Notification: " + notification);
		NotificationLine item = new NotificationLine(notification, Main.Multiplayer.config.notificationDisplaySeconds);
		currentMessages.Insert(0, item);
		if (currentMessages.Count > 4)
		{
			currentMessages.RemoveAt(currentMessages.Count - 1);
		}
	}

	public void DisplayProgressNotification(string playerName, ProgressUpdate progress)
	{
		string progressNotification = Main.Multiplayer.ProgressManager.GetProgressNotification(progress);
		if (progressNotification != null)
		{
			DisplayNotification(playerName + " " + progressNotification);
		}
	}

	public void Update()
	{
		if (messageBox == null)
		{
			return;
		}
		float num = 0f;
		float num2 = Main.Multiplayer.config.notificationDisplaySeconds / 2f;
		for (int i = 0; i < textLines.Length; i++)
		{
			if (i >= currentMessages.Count)
			{
				textLines[i].text = string.Empty;
				continue;
			}
			NotificationLine notificationLine = currentMessages[i];
			textLines[i].text = notificationLine.Text;
			if (textLines[i].preferredWidth > num)
			{
				num = textLines[i].preferredWidth;
			}
			notificationLine.SubtractTime(Time.unscaledDeltaTime);
			if (notificationLine.TimeLeft <= 0f)
			{
				currentMessages.RemoveAt(i);
			}
			else if (notificationLine.TimeLeft <= num2)
			{
				textLines[i].color = new Color(1f, 1f, 1f, notificationLine.TimeLeft / num2);
			}
			else
			{
				textLines[i].color = Color.white;
			}
		}
		if (num > 0f)
		{
			num += 10f;
		}
		messageBox.sizeDelta = new Vector2(num, currentMessages.Count * 20);
	}

	public void LevelLoaded()
	{
		if (messageBox == null)
		{
			CreateUI();
		}
	}

	private void CreateUI()
	{
		ModLog.Info("Creating new message box!");
		Transform transform = null;
		Canvas[] array = Object.FindObjectsOfType<Canvas>();
		foreach (Canvas canvas in array)
		{
			if (canvas.name == "Game UI")
			{
				transform = canvas.transform;
				break;
			}
		}
		if (transform == null)
		{
			return;
		}
		GameObject gameObject = null;
		PlayerPurgePoints[] array2 = Object.FindObjectsOfType<PlayerPurgePoints>();
		foreach (PlayerPurgePoints playerPurgePoints in array2)
		{
			if (((Object)(object)playerPurgePoints).name == "PurgePoints")
			{
				gameObject = ((Component)(object)playerPurgePoints).transform.GetChild(1).gameObject;
				break;
			}
		}
		if (!(gameObject == null))
		{
			GameObject gameObject2 = new GameObject("Message box", typeof(RectTransform), typeof(Image));
			RectTransform component = gameObject2.GetComponent<RectTransform>();
			component.SetParent(transform, worldPositionStays: false);
			SetOrientation(component, new Vector2(1f, 0f));
			component.sizeDelta = new Vector2(0f, 0f);
			gameObject2.GetComponent<Image>().color = new Color(0.17f, 0.17f, 0.17f, 0.8f);
			textLines = new Text[4];
			for (int j = 0; j < 4; j++)
			{
				Text component2 = Object.Instantiate(gameObject, component).GetComponent<Text>();
				SetOrientation(component2.rectTransform, Vector2.zero);
				component2.rectTransform.sizeDelta = new Vector2(100f, 20f);
				component2.rectTransform.anchoredPosition = new Vector2(5f, 2.5f + (float)(j * 20));
				component2.raycastTarget = false;
				component2.text = string.Empty;
				component2.horizontalOverflow = HorizontalWrapMode.Overflow;
				component2.alignment = TextAnchor.LowerLeft;
				component2.color = Color.white;
				textLines[j] = component2;
			}
			messageBox = component;
		}
		static void SetOrientation(RectTransform rect, Vector2 orient)
		{
			rect.anchorMin = orient;
			rect.anchorMax = orient;
			rect.pivot = orient;
			rect.anchoredPosition = Vector2.zero;
		}
	}
}
