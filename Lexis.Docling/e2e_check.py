from app import config  # noqa
import copy, json, sys, time, difflib
from app.converter import converter, warm_up
from hierarchical.postprocessor import ResultPostprocessor
from app import page_export
from app.postprocess import build_pages

pdf = sys.argv[1]
warm_up()
result = converter.convert(pdf)
ResultPostprocessor(result, source=pdf).process()

outs = []
for flag in (False, True, False):
    page_export.FAST_EXPORT = flag
    doc = copy.deepcopy(result.document)
    t = time.perf_counter()
    outs.append(build_pages(doc, pdf))
    print(f"E2E flag={'on ' if flag else 'off'} build_pages {time.perf_counter() - t:.1f}s")

def dump(o): return json.dumps(o, sort_keys=True, default=str, ensure_ascii=False)
off1, on, off2 = map(dump, outs)
print("E2E determinism (off vs off):", off1 == off2)
print("E2E fast == slow:", off1 == on, f"(pages: {len(outs[0])})")
if off1 != on:
    for pa, pb in zip(outs[0], outs[1]):
        for k in pa:
            if pa[k] != pb[k]:
                print("E2E first difference: page", pa["pageNumber"], "field", k)
                print("\n".join(list(difflib.unified_diff(
                    str(pa[k]).splitlines(), str(pb[k]).splitlines(), lineterm=""))[:15]))
                raise SystemExit