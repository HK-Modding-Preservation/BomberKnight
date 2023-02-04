using BomberKnight;
using BomberKnight.Enums;
using BomberKnight.Helper;
using ItemChanger.Extensions;
using ItemChanger.FsmStateActions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using UnityEngine;
using static tk2dSpriteCollectionDefinition;
using static UnityEngine.GraphicsBuffer;
using Color = UnityEngine.Color;

namespace LoreMaster.UnityComponents;

public class Bomb : MonoBehaviour
{
    #region Event Data

    public delegate void BombTrigger(BombEventArgs bombEventArgs);

    /// <summary>
    /// Fired when the bomb is placed before it's type is processed.
    /// </summary>
    public static event BombTrigger BombSpawned;

    /// <summary>
    /// Fired right after the explosion is activated and before the bomb object is removed.
    /// </summary>
    public static event BombTrigger BombExploded;

    #endregion

    #region Members

    private static GameObject _cloud;
    private bool _isHoming;
    private Rigidbody2D _rigidBody;
    private int _echoStack = 0;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the explosion object which will be created upon destruction.
    /// </summary>
    public static GameObject Explosion { get; set; }

    /// <summary>
    /// Gets or sets the type of the bomb to apply special effects.
    /// </summary>
    public BombType Type { get; set; }

    /// <summary>
    /// Gets the cloud object
    /// </summary>
    public static GameObject Cloud => _cloud == null ? _cloud = GameObject.Find("_GameManager").transform.Find("GlobalPool/Knight Spore Cloud(Clone)").gameObject : _cloud;

    #endregion

    #region Unity Methods

    void Start() => StartCoroutine(Ticking());

    void OnCollisionEnter2D(Collision2D collision)
    {
        // Ignore hero.
        if (collision.gameObject.name == "Knight" || (collision.gameObject.layer == 8 && _isHoming))
            Physics2D.IgnoreCollision(collision.collider, GetComponent<CircleCollider2D>());
    }

    #endregion

    #region Methods

    private IEnumerator Ticking()
    {
        BombSpawned?.Invoke(new(Type, transform.localPosition));
        float passedTime = 0f;
        float passedMilestone = 0f; // Used to blink faster over time.
        Color bombColor = Type switch
        {
            BombType.GrassBomb => Color.green,
            BombType.SporeBomb => new(1f, 0.4f, 0f),
            BombType.GoldBomb => Color.yellow,
            BombType.EchoBomb => new(1f, 0f, 1f),
            BombType.BounceBomb => Color.white,
            _ => Color.cyan
        };
        Color currentColor = bombColor;
        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.color = currentColor;

        (GameObject, float) homingData = CheckForHoming();
        while (passedTime < 3f)
        {
            if ((passedMilestone >= 0.5f && passedTime < 1f)
                || (passedMilestone >= .25f && passedTime >= 1f && passedTime <= 2f)
                || (passedMilestone >= .125f && passedTime > 2f))
            {
                passedMilestone = 0f;
                currentColor = currentColor == bombColor ? Color.red : bombColor;
                spriteRenderer.color = currentColor;
            }
            passedMilestone += Time.deltaTime;
            passedTime += Time.deltaTime;

            Homing(homingData);
            yield return null;
        }

        // Boom
        Explode(bombColor);

        GameManager.instance.StartCoroutine(FixGround());
        if (Type != BombType.EchoBomb)
            GameObject.Destroy(gameObject);
        else
        { 
            spriteRenderer.color = new(0f, 0f, 0f, 0f);
            StartCoroutine(Repeat());
        }
    }

    private static IEnumerator FixGround()
    {
        yield return new WaitForSeconds(0.2f);
        PlayMakerFSM.BroadcastEvent("VANISHED");
    }

    private IEnumerator Repeat()
    {
        for (_echoStack = 1; _echoStack < 5; _echoStack++)
        {
            GetComponent<SpriteRenderer>().color = new(1f, 0f, 1f, 1f - 0.2f * _echoStack);
            yield return new WaitForSeconds(3f);
            Explode(new(1f, 0f, 1f));
        }
        GameObject.Destroy(gameObject);
    }

    /// <summary>
    /// Check if the bomb should move to an enemy.
    /// </summary>
    private (GameObject, float) CheckForHoming()
    {
        GameObject enemyToChase = null;
        float homingSpeed = PlayerData.instance.GetBool(nameof(PlayerData.instance.equippedCharm_7))
                ? 15f
                : 10f;
        if (PlayerData.instance.GetBool(nameof(PlayerData.instance.equippedCharm_28)))
        {
            _rigidBody = GetComponent<Rigidbody2D>();

            HealthManager[] enemies = GameObject.FindObjectsOfType<HealthManager>();
            if (enemies != null && enemies.Any())
            {
                // Only enemies less than 100 units away can be targeted. Pick the nearest.
                float nearestDistance = 100f;
                foreach (HealthManager enemy in enemies)
                {
                    float distance = (enemy.transform.position - transform.localPosition).sqrMagnitude;
                    if (distance <= nearestDistance)
                    {
                        nearestDistance = distance;
                        enemyToChase = enemy.gameObject;
                        _rigidBody.mass = 1f;
                        _rigidBody.gravityScale = 0f;
                    }
                }
            }
        }
        _isHoming = enemyToChase != null;
        return new(enemyToChase, homingSpeed);
    }

    /// <summary>
    /// Move the bomb slowly to the passed target.
    /// </summary>
    private void Homing((GameObject, float) homingData)
    {
        if (homingData.Item1 == null)
            return;
        transform.position = Vector3.MoveTowards(transform.position, homingData.Item1.transform.position, homingData.Item2 * Time.deltaTime);

        if (transform.position.x < homingData.Item1.transform.position.x)
            transform.SetRotationZ(transform.localEulerAngles.z + 240f * Time.deltaTime);
        else
            transform.SetRotationZ(transform.localEulerAngles.z - 240f * Time.deltaTime);
    }

    private void Explode(Color bombColor)
    {
        GameObject explosion = GameObject.Instantiate(Explosion);
        explosion.name = Type + " Explosion";
        explosion.SetActive(false);

        // Color explosion
        ParticleSystem.MainModule settings = explosion.GetComponentInChildren<ParticleSystem>().main;
        settings.startColor = new ParticleSystem.MinMaxGradient(bombColor);
        explosion.GetComponentInChildren<SpriteRenderer>().color = bombColor != Color.white
            ? bombColor
            : new(0f, 0f, 0f);
        explosion.GetComponentInChildren<SimpleSpriteFade>().fadeInColor = bombColor != Color.white
            ? new(bombColor.r, bombColor.g, bombColor.b, 0f)
            : new(0f, 0f, 0f, 0f);

        typeof(SimpleSpriteFade).GetField("normalColor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(explosion.GetComponentInChildren<SimpleSpriteFade>(), bombColor != Color.white
            ? bombColor
            : new(0f, 0f, 0f));

        CalculateDamage(explosion);

        explosion.transform.localPosition = transform.localPosition;
        explosion.transform.localScale = Type switch
        {
            BombType.PowerBomb => new(2f, 2f, explosion.transform.localScale.z),
            BombType.BounceBomb => new(.75f, .75f, explosion.transform.localScale.z),
            _ => new(1.2f, 1.2f, explosion.transform.localScale.z)
        };

        explosion.GetComponent<CircleCollider2D>().isTrigger = true;
        explosion.AddComponent<Rigidbody2D>().gravityScale = 0f;

        if (Type == BombType.GoldBomb)
        {
            bool hasGreed = PlayerData.instance.GetBool(nameof(PlayerData.instance.equippedCharm_24));
            FlingGeoAction.SpawnGeo(UnityEngine.Random.Range(1, hasGreed ? 100 : 25), hasGreed ? 10 : 5, 0, ItemChanger.FlingType.Everywhere, explosion.transform);
        }
        else if (Type == BombType.SporeBomb)
            GameManager.instance.StartCoroutine(SporeCloud());
        
        explosion.SetActive(true);
        BombExploded?.Invoke(new(Type, transform.localPosition));

        if (Type == BombType.PowerBomb)
            PlayMakerFSM.BroadcastEvent("POWERBOMBED");
        else
            PlayMakerFSM.BroadcastEvent("BOMBED");
    }

    private void CalculateDamage(GameObject explosion)
    {
        float damage = Type switch
        {
            BombType.GrassBomb => 20,
            BombType.GoldBomb => Mathf.Min(PlayerData.instance.GetBool(nameof(PlayerData.instance.equippedCharm_24))
            ? 100 : 50,
            PlayerData.instance.GetInt("geo") / 100 + 5),
            BombType.BounceBomb => 1,
            BombType.PowerBomb => 40,
            BombType.EchoBomb => 10 * (1 - _echoStack * .2f),
            _ => 10
        };

        // Bonus damage with shaman stone
        if (PlayerData.instance.GetBool(nameof(PlayerData.instance.equippedCharm_19)))
            damage *= 1.2f;

        explosion.LocateMyFSM("damages_enemy").FsmVariables.FindFsmInt("damageDealt").Value = Convert.ToInt32(damage);
    }

    private IEnumerator SporeCloud()
    {
        GameObject newCloud = GameObject.Instantiate(Cloud, transform.position,
                Quaternion.identity);
        newCloud.SetActive(true);
        newCloud.LocateMyFSM("Control")
            .GetState("Init")
            .AdjustTransition("NORMAL", "Deep");
        yield return new WaitForSeconds(4.1f);
        GameObject.Destroy(newCloud);
    }

    #endregion

}
