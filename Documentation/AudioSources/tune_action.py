from pathlib import Path
import re
values={'walkSpeed':4.8,'sprintSpeed':9.2,'crouchSpeed':2.6,'groundAcceleration':42,'groundDeceleration':48,'airControl':.45,'strafeSpeedMultiplier':.95,'backwardSpeedMultiplier':.82,'sprintStaminaDrain':14,'staminaRecovery':30,'stanceTransitionSpeed':11,'slideSpeedBoost':.8,'slideFriction':7.5}
p=Path('Assets/scripts/PlayerController.cs');s=p.read_text()
for k,v in values.items():s=re.sub(r'(private float '+k+r' = )[-.\d]+f;',lambda m:m[1]+str(v)+'f;',s)
p.write_text(s)
for name in ['Player','Bot']:
 p=Path('Assets/Resources/'+name+'.prefab');s=p.read_text()
 for k,v in values.items():s=re.sub(r'(^  '+k+r': )[^\r\n]+',lambda m:m[1]+str(v),s,flags=re.M)
 s=re.sub(r'(^  cycleDistance: )[^\n]+',r'\g<1>2.3',s,flags=re.M)
 s=re.sub(r'(^  runCycleDistance: )[^\n]+',r'\g<1>3.6',s,flags=re.M)
 s=s.replace('  maximumPositionOffset: 0.015','  maximumPositionOffset: 0.02').replace('  smoothness: 14','  smoothness: 18')
 # The player prefab had bob disabled; enable only the human component.
 blocks=re.split(r'(?=^--- !u!)',s,flags=re.M)
 for i,b in enumerate(blocks):
  if 'guid: b703ae0ad44548f0b19f141c998668cd' in b:
   if name=='Player':b=b.replace('  m_Enabled: 0','  m_Enabled: 1')
   b=b.replace('walkFrequency: 8','walkFrequency: 10').replace('sprintFrequency: 12','sprintFrequency: 15').replace('walkAmplitude: 0.035','walkAmplitude: 0.045').replace('sprintAmplitude: 0.055','sprintAmplitude: 0.08')
   blocks[i]=b
 p.write_text(''.join(blocks))
p=Path('Assets/scripts/PlayerCameraLook.cs');s=p.read_text().replace('strafeRoll = 1.2f','strafeRoll = 1.7f').replace('walkRoll = .35f','walkRoll = .5f').replace('sprintRoll = .65f','sprintRoll = 1.05f').replace('rollSmoothTime = .18f','rollSmoothTime = .12f');p.write_text(s)
p=Path('Assets/scripts/PlayerHeadBob.cs');s=p.read_text().replace('walkFrequency = 8f','walkFrequency = 10f').replace('sprintFrequency = 12f','sprintFrequency = 15f').replace('walkAmplitude = 0.035f','walkAmplitude = 0.045f').replace('sprintAmplitude = 0.055f','sprintAmplitude = 0.08f').replace('sprintFrequency * .5f','sprintFrequency * .8f').replace('horizontalVelocity.magnitude / 3.2f','horizontalVelocity.magnitude / 4.8f');p.write_text(s)
p=Path('Assets/scripts/PlayerWalkAnimation.cs');s=p.read_text().replace('cycleDistance = 1.65f','cycleDistance = 2.3f').replace('runCycleDistance = 2.15f','runCycleDistance = 3.6f');p.write_text(s)
p=Path('Assets/scripts/WeaponSway.cs');s=p.read_text().replace('maximumPositionOffset = 0.015f','maximumPositionOffset = 0.02f').replace('smoothness = 14f','smoothness = 18f');p.write_text(s)
p=Path('Assets/Anims/Ak74/AK74_Handling.asset');s=p.read_text().replace('lowErgonomicsAimTime: 0.5','lowErgonomicsAimTime: 0.38').replace('highErgonomicsAimTime: 0.16','highErgonomicsAimTime: 0.12').replace('aimOutTimeMultiplier: 0.8','aimOutTimeMultiplier: 0.65').replace('sprintBlendTime: 0.22','sprintBlendTime: 0.14').replace('sprintBob: 0.012','sprintBob: 0.016');p.write_text(s)
