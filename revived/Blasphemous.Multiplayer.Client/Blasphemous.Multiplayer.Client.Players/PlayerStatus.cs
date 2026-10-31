using System.Collections;
using Framework.Managers;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client.Players;

public class PlayerStatus
{
	public enum SkinStatus
	{
		Updated,
		YesUpdate,
		NoUpdate
	}

	private string _currentScene;

	private string _lastMapScene;

	private Texture2D _skinTexture;

	private static readonly string[] originalSkins = new string[18]
	{
		"PENITENT_DEFAULT", "PENITENT_ENDING_A", "PENITENT_ENDING_B", "PENITENT_OSSUARY", "PENITENT_BACKER", "PENITENT_DELUXE", "PENITENT_ALMS", "PENITENT_PE01", "PENITENT_PE02", "PENITENT_PE03",
		"PENITENT_BOSSRUSH", "PENITENT_DEMAKE", "PENITENT_ENDING_C", "PENITENT_SIERPES", "PENITENT_ISIDORA", "PENITENT_BOSSRUSH_S", "PENITENT_GAMEBOY", "PENITENT_KONAMI"
	};

	public string Name { get; private set; }

	public string CurrentScene
	{
		get
		{
			return _currentScene;
		}
		set
		{
			_currentScene = value;
			if (value.Length == 9)
			{
				_lastMapScene = value;
			}
		}
	}

	public string LastMapScene => _lastMapScene;

	public byte Team { get; set; }

	public byte SpecialAnimation { get; set; }

	public ushort Ping { get; set; }

	public Texture2D SkinTexture => _skinTexture;

	public SkinStatus SkinUpdateStatus { get; set; }

	public PlayerStatus(string name)
	{
		Name = name;
		Team = 1;
		SpecialAnimation = 0;
		_currentScene = string.Empty;
		_lastMapScene = string.Empty;
		SetSkinTexture("PENITENT_DEFAULT");
		SkinUpdateStatus = SkinStatus.Updated;
	}

	public void SetSkinTexture(string skinName)
	{
		_skinTexture = Core.ColorPaletteManager.GetColorPaletteById(skinName).texture;
	}

	public void SetSkinTexture(byte[] skinData)
	{
		Main.Instance.StartCoroutine(DelaySkinUpdate());
		IEnumerator DelaySkinUpdate()
		{
			yield return new WaitForEndOfFrame();
			yield return new WaitForEndOfFrame();
			Texture2D texture2D = new Texture2D(256, 1, TextureFormat.RGB24, mipmap: false);
			texture2D.LoadImage(skinData);
			texture2D.filterMode = FilterMode.Point;
			_skinTexture = texture2D;
		}
	}

	public static bool IsOriginalSkin(string skinName)
	{
		string[] array = originalSkins;
		foreach (string text in array)
		{
			if (skinName == text)
			{
				return true;
			}
		}
		return false;
	}
}
