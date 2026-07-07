using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class StanceVignette : MonoBehaviour
{
    [SerializeField] private float minIntensity = 0.1f;
    [SerializeField] private float maxIntensity = 0.35f;
    [SerializeField] private float responseSpeed = 10f;
    private VolumeProfile _profile;
    private Vignette _vignette;
    public void Initialize(VolumeProfile volumeProfile)
    {
        _profile = volumeProfile;

        if (!volumeProfile.TryGet(out _vignette))
            _vignette = volumeProfile.Add<Vignette>();
        
        _vignette.intensity.Override(minIntensity);
    }

    public void UpdateVignette(float deltaTime, PlayerStance playerStance)
    {
        var targetIntensity = playerStance is PlayerStance.Standing
            ? minIntensity
            : maxIntensity;

        _vignette.intensity.value = Mathf.Lerp(
            a: _vignette.intensity.value,
            b: targetIntensity,
            t: 1f - Mathf.Exp(-responseSpeed * deltaTime)
        );
    }
}
