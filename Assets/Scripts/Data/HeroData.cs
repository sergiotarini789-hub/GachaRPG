using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewHero", menuName = "GachaRPG/Hero Data")]
public class HeroData : ScriptableObject
{
    public string heroName;
    public HeroRarity rarity;
    public int baseHealth;
    public int baseAttack;
    public int baseDefense;
    public int baseSpeed;
    public Sprite icon;
    public string description;

    /// <summary>Skill kit the hero brings into battle (assign SkillData assets in the Inspector).</summary>
    public List<SkillData> skills;
}
