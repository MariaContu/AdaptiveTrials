"""Build final player profiles with macro and granular features."""
from __future__ import annotations
import argparse, csv, json, logging, math, sys
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_GAMES = ROOT / 'data/interim/final_player_games_1000.csv'
DEFAULT_MAPPING = ROOT / 'data/interim/final_game_category_scores_v2.csv'
DEFAULT_STATUS = ROOT / 'data/interim/final_player_library_status_1000.csv'
DEFAULT_INCLUSIVE = ROOT / 'data/processed/final_player_profiles_inclusive.csv'
DEFAULT_EXCLUDED = ROOT / 'data/processed/final_player_profiles_source_excluded.csv'
DEFAULT_COMPARISON = ROOT / 'data/processed/final_player_profile_source_effect.csv'
DEFAULT_SUMMARY = ROOT / 'reports/metrics/06_final_player_profiles_summary.json'
LOGGER = logging.getLogger('final-player-profiles')
MACRO_CATEGORIES = ('combat','exploration','strategic_reasoning')
SUBGROUPS = {
 'combat': ('shooter','melee','action_other'),
 'exploration': ('open_world','narrative_exploration','investigation'),
 'strategic_reasoning': ('logic_puzzle','planning_management','strategy_decision','observation_deduction'),
}

def norm(v: Any) -> str: return '' if v is None else str(v).strip()
def safe_int(v: Any) -> int:
    try: return int(float(v))
    except (TypeError, ValueError): return 0
def safe_float(v: Any) -> float:
    try: return float(v)
    except (TypeError, ValueError): return 0.0

def read_csv(path: Path):
    if not path.exists(): raise FileNotFoundError(path)
    with path.open('r', encoding='utf-8', newline='') as f:
        r=csv.DictReader(f)
        if r.fieldnames is None: raise ValueError(f'CSV has no header: {path}')
        return list(r.fieldnames), [dict(x) for x in r]

def write_csv(path: Path, fields: list[str], rows: list[dict[str,Any]]):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp=path.with_suffix(path.suffix+'.tmp')
    with tmp.open('w', encoding='utf-8', newline='') as f:
        w=csv.DictWriter(f, fieldnames=fields, extrasaction='ignore'); w.writeheader(); w.writerows(rows)
    tmp.replace(path)

def write_json(path: Path, payload: dict[str,Any]):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp=path.with_suffix(path.suffix+'.tmp')
    with tmp.open('w', encoding='utf-8') as f: json.dump(payload,f,ensure_ascii=False,indent=2)
    tmp.replace(path)

def parse_source_appids(v: Any) -> set[int]:
    text=norm(v)
    if not text: return set()
    parts=[text]
    for sep in (';','|',','):
        if sep in text:
            parts=[p.strip() for p in text.split(sep)]; break
    return {safe_int(p) for p in parts if safe_int(p)>0}

def entropy(values: list[float]):
    positive=[v for v in values if v>0]
    if not positive: return 0.0,0.0
    total=sum(positive); probs=[v/total for v in positive]
    raw=-sum(p*math.log2(p) for p in probs)
    maxv=math.log2(len(values)) if len(values)>1 else 0.0
    return raw, raw/maxv if maxv>0 else 0.0

def resolve_target(shares: dict[str,float], coverage: float, min_cov: float, min_share: float, min_gap: float):
    ordered=sorted(shares.items(), key=lambda x:(-x[1],x[0]))
    top_name,top=ordered[0]; second=ordered[1][1] if len(ordered)>1 else 0.0; gap=top-second
    if coverage<min_cov: return 'unresolved',False,'insufficient_coverage',top,second,gap
    if top<min_share: return 'unresolved',False,'insufficient_dominance',top,second,gap
    if gap<min_gap: return 'unresolved',False,'insufficient_gap',top,second,gap
    return top_name,True,'resolved',top,second,gap

def load_mapping(path: Path):
    fields,rows=read_csv(path)
    req={'appid','assigned_category','assigned_subgroup'}
    if req-set(fields): raise ValueError(f'Mapping missing: {sorted(req-set(fields))}')
    return {safe_int(r['appid']):r for r in rows if safe_int(r.get('appid'))>0}

def load_status(path: Path):
    fields,rows=read_csv(path)
    req={'final_player_id','final_sampling_source','source_group','source_appid','source_game'}
    if req-set(fields): raise ValueError(f'Status missing: {sorted(req-set(fields))}')
    result={norm(r['final_player_id']):r for r in rows if norm(r.get('final_player_id'))}
    if len(result)!=1000: raise ValueError(f'Expected 1000 status profiles, found {len(result)}')
    return result

def load_games(path: Path):
    fields,rows=read_csv(path)
    req={'final_player_id','appid','playtime_forever_minutes'}
    if req-set(fields): raise ValueError(f'Games missing: {sorted(req-set(fields))}')
    grouped=defaultdict(list)
    for r in rows:
        pid=norm(r.get('final_player_id'))
        if pid: grouped[pid].append(r)
    if len(grouped)!=1000: raise ValueError(f'Expected games for 1000 players, found {len(grouped)}')
    return grouped

def make_fields():
    fields=['final_player_id','final_sampling_source','source_group','source_appid','source_game','profile_variant',
    'total_library_games','total_played_games','total_playtime_minutes','total_playtime_hours','categorized_games','categorized_played_games','categorized_playtime_minutes','categorized_playtime_hours','category_game_coverage','category_playtime_coverage','avg_playtime_per_played_game_minutes']
    for c in MACRO_CATEGORIES:
        fields += [f'games_{c}',f'played_games_{c}',f'playtime_{c}_minutes',f'hours_{c}',f'share_{c}']
    fields += ['diversity','entropy','normalized_entropy','dominance','second_max','gap','macro_target','macro_target_resolved','macro_target_reason']
    for c,subs in SUBGROUPS.items():
        fields += [f'{c}_subgroup_playtime_coverage',f'{c}_subgroup_target',f'{c}_subgroup_target_resolved',f'{c}_subgroup_target_reason']
        for s in subs:
            fields += [f'games_{s}',f'played_games_{s}',f'playtime_{s}_minutes',f'hours_{s}',f'share_{s}_within_{c}']
    return fields
PROFILE_FIELDS=make_fields()
COMPARISON_FIELDS=['final_player_id','final_sampling_source','source_group','source_appid','source_game','inclusive_macro_target','excluded_macro_target','macro_target_changed','inclusive_macro_resolved','excluded_macro_resolved','inclusive_dominance','excluded_dominance','dominance_change','inclusive_gap','excluded_gap','gap_change','removed_game_rows','removed_playtime_minutes','removed_playtime_share']

def build_profile(pid, game_rows, status, mapping, variant, exclude_source, args):
    source_appids=parse_source_appids(status.get('source_appid'))
    included=[]; removed=[]
    for row in game_rows:
        if exclude_source and safe_int(row.get('appid')) in source_appids: removed.append(row)
        else: included.append(row)
    total_games=len(included); total_played=sum(safe_int(r.get('playtime_forever_minutes'))>0 for r in included); total_pt=sum(safe_int(r.get('playtime_forever_minutes')) for r in included)
    mg=Counter(); mpg=Counter(); mpt=Counter(); sg=Counter(); spg=Counter(); spt=Counter(); subcat_pt=Counter()
    categorized_games=categorized_played=categorized_pt=0
    for row in included:
        appid=safe_int(row.get('appid')); pt=safe_int(row.get('playtime_forever_minutes')); mapped=mapping.get(appid)
        if not mapped: continue
        cat=norm(mapped.get('assigned_category')); sub=norm(mapped.get('assigned_subgroup'))
        if cat not in MACRO_CATEGORIES: continue
        categorized_games+=1; mg[cat]+=1; mpt[cat]+=pt
        if pt>0: categorized_played+=1; mpg[cat]+=1; categorized_pt+=pt
        if sub in SUBGROUPS[cat]:
            sg[sub]+=1; spt[sub]+=pt; subcat_pt[cat]+=pt
            if pt>0: spg[sub]+=1
    shares={c:(mpt[c]/categorized_pt if categorized_pt else 0.0) for c in MACRO_CATEGORIES}
    game_cov=categorized_games/total_games if total_games else 0.0
    pt_cov=categorized_pt/total_pt if total_pt else 0.0
    target,resolved,reason,dom,second,gap=resolve_target(shares,pt_cov,args.minimum_categorized_playtime_coverage,args.minimum_target_share,args.minimum_target_gap)
    ent,nent=entropy([shares[c] for c in MACRO_CATEGORIES])
    profile={'final_player_id':pid,'final_sampling_source':norm(status.get('final_sampling_source')),'source_group':norm(status.get('source_group')),'source_appid':norm(status.get('source_appid')),'source_game':norm(status.get('source_game')),'profile_variant':variant,'total_library_games':total_games,'total_played_games':total_played,'total_playtime_minutes':total_pt,'total_playtime_hours':round(total_pt/60,6),'categorized_games':categorized_games,'categorized_played_games':categorized_played,'categorized_playtime_minutes':categorized_pt,'categorized_playtime_hours':round(categorized_pt/60,6),'category_game_coverage':round(game_cov,6),'category_playtime_coverage':round(pt_cov,6),'avg_playtime_per_played_game_minutes':round(total_pt/total_played,6) if total_played else 0.0,'diversity':sum(mpt[c]>0 for c in MACRO_CATEGORIES),'entropy':round(ent,6),'normalized_entropy':round(nent,6),'dominance':round(dom,6),'second_max':round(second,6),'gap':round(gap,6),'macro_target':target,'macro_target_resolved':resolved,'macro_target_reason':reason}
    for c in MACRO_CATEGORIES:
        profile[f'games_{c}']=mg[c]; profile[f'played_games_{c}']=mpg[c]; profile[f'playtime_{c}_minutes']=mpt[c]; profile[f'hours_{c}']=round(mpt[c]/60,6); profile[f'share_{c}']=round(shares[c],6)
    for c,subs in SUBGROUPS.items():
        cat_pt=mpt[c]; resolved_sub_pt=subcat_pt[c]; sub_cov=resolved_sub_pt/cat_pt if cat_pt else 0.0
        subshares={s:(spt[s]/resolved_sub_pt if resolved_sub_pt else 0.0) for s in subs}
        st,sr,sreason,_,_,_=resolve_target(subshares,sub_cov,args.minimum_subgroup_playtime_coverage,args.minimum_subgroup_share,args.minimum_subgroup_gap)
        profile[f'{c}_subgroup_playtime_coverage']=round(sub_cov,6); profile[f'{c}_subgroup_target']=st; profile[f'{c}_subgroup_target_resolved']=sr; profile[f'{c}_subgroup_target_reason']=sreason
        for s in subs:
            profile[f'games_{s}']=sg[s]; profile[f'played_games_{s}']=spg[s]; profile[f'playtime_{s}_minutes']=spt[s]; profile[f'hours_{s}']=round(spt[s]/60,6); profile[f'share_{s}_within_{c}']=round(subshares[s],6)
    removed_pt=sum(safe_int(r.get('playtime_forever_minutes')) for r in removed)
    return profile, {'removed_game_rows':len(removed),'removed_playtime_minutes':removed_pt,'removed_playtime_share':removed_pt/(removed_pt+total_pt) if removed_pt+total_pt else 0.0}

def summarize(rows):
    return {'profiles':len(rows),'macro_target_counts':dict(sorted(Counter(str(r['macro_target']) for r in rows).items())),'macro_target_reason_counts':dict(sorted(Counter(str(r['macro_target_reason']) for r in rows).items())),'resolved_macro_profiles':sum(bool(r['macro_target_resolved']) for r in rows),'unresolved_macro_profiles':sum(not bool(r['macro_target_resolved']) for r in rows),'subgroup_target_counts':{c:dict(sorted(Counter(str(r[f'{c}_subgroup_target']) for r in rows).items())) for c in MACRO_CATEGORIES},'mean_category_playtime_coverage':round(sum(safe_float(r['category_playtime_coverage']) for r in rows)/len(rows),6) if rows else 0.0}

def main():
    logging.basicConfig(level=logging.INFO, format='%(asctime)s | %(levelname)s | %(message)s')
    p=argparse.ArgumentParser(); p.add_argument('--games',type=Path,default=DEFAULT_GAMES); p.add_argument('--mapping',type=Path,default=DEFAULT_MAPPING); p.add_argument('--status',type=Path,default=DEFAULT_STATUS); p.add_argument('--inclusive-output',type=Path,default=DEFAULT_INCLUSIVE); p.add_argument('--excluded-output',type=Path,default=DEFAULT_EXCLUDED); p.add_argument('--comparison-output',type=Path,default=DEFAULT_COMPARISON); p.add_argument('--summary-output',type=Path,default=DEFAULT_SUMMARY); p.add_argument('--minimum-categorized-playtime-coverage',type=float,default=0.70); p.add_argument('--minimum-target-share',type=float,default=0.40); p.add_argument('--minimum-target-gap',type=float,default=0.10); p.add_argument('--minimum-subgroup-playtime-coverage',type=float,default=0.60); p.add_argument('--minimum-subgroup-share',type=float,default=0.40); p.add_argument('--minimum-subgroup-gap',type=float,default=0.10); args=p.parse_args()
    started=datetime.now(timezone.utc)
    try:
        mapping=load_mapping(args.mapping); statuses=load_status(args.status); games=load_games(args.games)
        inc=[]; exc=[]; comp=[]
        for pid in sorted(statuses):
            a,_=build_profile(pid,games[pid],statuses[pid],mapping,'inclusive',False,args)
            b,rem=build_profile(pid,games[pid],statuses[pid],mapping,'source_excluded',True,args)
            inc.append(a); exc.append(b)
            comp.append({'final_player_id':pid,'final_sampling_source':a['final_sampling_source'],'source_group':a['source_group'],'source_appid':a['source_appid'],'source_game':a['source_game'],'inclusive_macro_target':a['macro_target'],'excluded_macro_target':b['macro_target'],'macro_target_changed':a['macro_target']!=b['macro_target'],'inclusive_macro_resolved':a['macro_target_resolved'],'excluded_macro_resolved':b['macro_target_resolved'],'inclusive_dominance':a['dominance'],'excluded_dominance':b['dominance'],'dominance_change':round(safe_float(b['dominance'])-safe_float(a['dominance']),6),'inclusive_gap':a['gap'],'excluded_gap':b['gap'],'gap_change':round(safe_float(b['gap'])-safe_float(a['gap']),6),'removed_game_rows':rem['removed_game_rows'],'removed_playtime_minutes':rem['removed_playtime_minutes'],'removed_playtime_share':round(rem['removed_playtime_share'],6)})
        write_csv(args.inclusive_output,PROFILE_FIELDS,inc); write_csv(args.excluded_output,PROFILE_FIELDS,exc); write_csv(args.comparison_output,COMPARISON_FIELDS,comp)
        changed=[r for r in comp if r['macro_target_changed']]
        finished=datetime.now(timezone.utc)
        summary={'pipeline_stage':'06_build_final_player_profiles','started_at':started.isoformat(),'finished_at':finished.isoformat(),'duration_seconds':round((finished-started).total_seconds(),3),'parameters':{'minimum_categorized_playtime_coverage':args.minimum_categorized_playtime_coverage,'minimum_target_share':args.minimum_target_share,'minimum_target_gap':args.minimum_target_gap,'minimum_subgroup_playtime_coverage':args.minimum_subgroup_playtime_coverage,'minimum_subgroup_share':args.minimum_subgroup_share,'minimum_subgroup_gap':args.minimum_subgroup_gap},'inclusive':summarize(inc),'source_excluded':summarize(exc),'source_game_effect':{'profiles_compared':len(comp),'profiles_with_source_game_removed':sum(safe_int(r['removed_game_rows'])>0 for r in comp),'macro_target_changes':len(changed),'macro_target_change_rate':round(len(changed)/len(comp),6),'changes_by_sampling_source':dict(sorted(Counter(r['final_sampling_source'] for r in changed).items())),'changes_by_source_group':dict(sorted(Counter(r['source_group'] for r in changed).items())),'mean_removed_playtime_share':round(sum(safe_float(r['removed_playtime_share']) for r in comp)/len(comp),6),'maximum_removed_playtime_share':round(max(safe_float(r['removed_playtime_share']) for r in comp),6)},'outputs':{'inclusive_profiles':str(args.inclusive_output),'source_excluded_profiles':str(args.excluded_output),'source_effect_comparison':str(args.comparison_output)},'methodological_note':'Sampling provenance is retained only for audit. Targets are derived from categorized playtime. Inclusive and source-excluded variants measure source-game leakage.'}
        write_json(args.summary_output,summary)
    except (OSError,ValueError) as e:
        LOGGER.exception('Final player profile construction failed: %s',e); return 1
    LOGGER.info('Profiles completed: inclusive=%d, excluded=%d, changes=%d.',len(inc),len(exc),len(changed)); LOGGER.info('Summary: %s',args.summary_output); return 0

if __name__=='__main__': sys.exit(main())