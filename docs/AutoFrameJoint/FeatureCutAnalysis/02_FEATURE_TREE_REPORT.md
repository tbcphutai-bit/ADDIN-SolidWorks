# Feature Tree Report

Order is the ModelDoc2.FirstFeature/GetNextFeature linked-list order, plus GetFirstSubFeature/GetNextSubFeature paths. It is not a UI screenshot or a claim that the visual UI tree has identical order. Absorbed Sketch nodes may also appear in the top-level chain; Cut counting de-duplicates a node by feature name and API type per Part. No configuration was activated through a setter.

## 1618-04-bh-A6-fr1.SLDPRT

Configuration: `Default`. Top-level nodes: 37. Evidence JSON: `evidence/api/1618-04-bh-A6-fr1.SLDPRT.json`.

| API path | Name | GetTypeName2 / legacy | Suppressed active | Parents |
| --- | --- | --- | --- | --- |
| 1 | Configuration Comments | ConfigCommentsFolder | False |  |
| 2 | Favorites | FavoriteFolder | False |  |
| 3 | History | HistoryFolder | False |  |
| 4 | Selection Sets | SelectionSetFolder | False |  |
| 5 | Sensors | SensorFolder | False |  |
| 6 | ﾃﾞｻﾞｲﾝ ﾊﾞｲﾝﾀﾞｰ | DocsFolder | False |  |
| 7 | Annotations | DetailCabinet | False |  |
| 7.1 | Notes | NotesAreaFtrFolder | False |  |
| 7.2 | Notes Area | AnnotationViewFeat | False |  |
| 7.3 | Notes1___EndTag___ | FtrFolder | False |  |
| 8 | Markups | InkMarkupFolder | False |  |
| 9 | Lights and Cameras | EnvFolder | False |  |
| 9.1 | Ambient | AmbientLight | False |  |
| 9.2 | Directional1 | DirectionLight | False |  |
| 10 | Solid Bodies | SolidBodyFolder | False |  |
| 10.1 | Sheet<1> | CutListFolder | False |  |
| 11 | Surface Bodies | SurfaceBodyFolder | False |  |
| 12 | Comments | CommentsFolder | False |  |
| 13 | Equations | EqnFolder | False |  |
| 14 | A1100- | MaterialFolder | False |  |
| 15 | Front | RefPlane | False |  |
| 16 | Top | RefPlane | False |  |
| 17 | Right | RefPlane | False |  |
| 18 | Origin | OriginProfileFeature | False | Front |
| 19 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 20 | 板金19 | SheetMetal | False | 板金 |
| 21 | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 | SMBaseFlange | False | 板金, 板金19, ｽｹｯﾁ1, Right |
| 21.1 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 21.2 | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.3 | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.4 | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 22 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 23 | ｶｯﾄ - 押し出し110 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 23.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 24 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し110, a |
| 25 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | True | ｶｯﾄ - 押し出し110, a |
| 26 | スタッド10x16<1> | LibraryFeature | True | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し110, ｶｰﾌﾞ ﾊﾟﾀｰﾝ1, ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 |
| 26.1 | ｶｯﾄ - 押し出し110 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 26.1.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 26.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し110, a |
| 26.3 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | True | ｶｯﾄ - 押し出し110, a |
| 27 | Sheet<1> | CutListFolder | False |  |
| 28 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 29 | ｶｯﾄ - 押し出し1 | Cut | False | ｽｹｯﾁ2, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 29.1 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 30 | ｽｹｯﾁ3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 31 | ｶｯﾄ - 押し出し2 | Cut | False | ｽｹｯﾁ3, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 31.1 | ｽｹｯﾁ3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 32 | ｽｹｯﾁ16 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し2 |
| 33 | ｶｯﾄ - 押し出し112 | Cut | False | ｽｹｯﾁ16, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し2 |
| 33.1 | ｽｹｯﾁ16 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し2 |
| 34 | M4 すきま穴1 | HoleWzd | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｽｹｯﾁ31 |
| 34.1 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 34.2 | ｽｹｯﾁ31 | ProfileFeature | False |  |
| 35 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern | False | M4 すきま穴1, a1 |
| 36 | M4-NCサラ(表)<1> | LibraryFeature | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｽｹｯﾁ31, M4 すきま穴1, ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 |
| 36.1 | M4 すきま穴1 | HoleWzd | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｽｹｯﾁ31 |
| 36.1.1 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 36.1.2 | ｽｹｯﾁ31 | ProfileFeature | False |  |
| 36.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern | False | M4 すきま穴1, a1 |
| 37 | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 | FlatPattern | True | 板金, 板金19, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 37.1 | ﾍﾞﾝﾄﾞ-ﾗｲﾝ2 | ProfileFeature | False |  |
| 37.2 | 境界ﾎﾞｯｸｽ2 | ProfileFeature | True |  |
| 37.3 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ1>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 37.4 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ3>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 37.5 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ4>9 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 37.6 | ｽｹｯﾁ変換1 | FoldUpDownSketchFolder | True |  |

## 1618-04-bh-A6-1.SLDPRT

Configuration: `Default`. Top-level nodes: 44. Evidence JSON: `evidence/api/1618-04-bh-A6-1.SLDPRT.json`.

| API path | Name | GetTypeName2 / legacy | Suppressed active | Parents |
| --- | --- | --- | --- | --- |
| 1 | Configuration Comments | ConfigCommentsFolder | False |  |
| 2 | Favorites | FavoriteFolder | False |  |
| 3 | History | HistoryFolder | False |  |
| 4 | Selection Sets | SelectionSetFolder | False |  |
| 5 | Sensors | SensorFolder | False |  |
| 6 | ﾃﾞｻﾞｲﾝ ﾊﾞｲﾝﾀﾞｰ | DocsFolder | False |  |
| 7 | Annotations | DetailCabinet | False |  |
| 7.1 | Notes | NotesAreaFtrFolder | False |  |
| 7.2 | Notes Area | AnnotationViewFeat | False |  |
| 7.3 | Notes1___EndTag___ | FtrFolder | False |  |
| 8 | Markups | InkMarkupFolder | False |  |
| 9 | Lights and Cameras | EnvFolder | False |  |
| 9.1 | Ambient | AmbientLight | False |  |
| 9.2 | Directional1 | DirectionLight | False |  |
| 10 | Solid Bodies | SolidBodyFolder | False |  |
| 10.1 | Sheet<1> | CutListFolder | False |  |
| 10.2 | Sheet<2> | CutListFolder | False |  |
| 11 | Surface Bodies | SurfaceBodyFolder | False |  |
| 12 | Comments | CommentsFolder | False |  |
| 13 | Equations | EqnFolder | False |  |
| 14 | A1100- | MaterialFolder | False |  |
| 15 | Front | RefPlane | False |  |
| 16 | Top | RefPlane | False |  |
| 17 | Right | RefPlane | False |  |
| 18 | Origin | OriginProfileFeature | False | Front |
| 19 | ｽｹｯﾁ1 | ProfileFeature | False | Top, Origin |
| 20 | 板金19 | SheetMetal | False | 板金 |
| 21 | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 | SMBaseFlange | False | 板金, 板金19, ｽｹｯﾁ1, Top |
| 21.1 | ｽｹｯﾁ1 | ProfileFeature | False | Top, Origin |
| 22 | Sheet<1> | CutListFolder | False |  |
| 23 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 24 | ﾎﾞｽ - 押し出し2 | ICE / Boss | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 24.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 25 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ﾎﾞｽ - 押し出し2, a |
| 26 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | True | ﾎﾞｽ - 押し出し2, a |
| 27 | スタッド7.5<1> | LibraryFeature | True | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾎﾞｽ - 押し出し2, ｶｰﾌﾞ ﾊﾟﾀｰﾝ1, ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 |
| 27.1 | ﾎﾞｽ - 押し出し2 | ICE / Boss | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 27.1.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 27.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ﾎﾞｽ - 押し出し2, a |
| 27.3 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | True | ﾎﾞｽ - 押し出し2, a |
| 28 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 29 | ﾎﾞｽ - 押し出し21 | ICE / Boss | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 29.1 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 30 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern | False | ﾎﾞｽ - 押し出し21, a1 |
| 31 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ21 | CurvePattern | True | ﾎﾞｽ - 押し出し21, a1 |
| 32 | スタッド7.5<2> | LibraryFeature | True | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾎﾞｽ - 押し出し21, ｶｰﾌﾞ ﾊﾟﾀｰﾝ11, ｶｰﾌﾞ ﾊﾟﾀｰﾝ21 |
| 32.1 | ﾎﾞｽ - 押し出し21 | ICE / Boss | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 32.1.1 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 32.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern | False | ﾎﾞｽ - 押し出し21, a1 |
| 32.3 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ21 | CurvePattern | True | ﾎﾞｽ - 押し出し21, a1 |
| 33 | a2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 34 | ﾎﾞｽ - 押し出し22 | ICE / Boss | False | a2, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 34.1 | a2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 35 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ12 | CurvePattern | False | ﾎﾞｽ - 押し出し22, a2 |
| 36 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ22 | CurvePattern | True | ﾎﾞｽ - 押し出し22, a2 |
| 37 | スタッド7.5<3> | LibraryFeature | True | a2, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾎﾞｽ - 押し出し22, ｶｰﾌﾞ ﾊﾟﾀｰﾝ12, ｶｰﾌﾞ ﾊﾟﾀｰﾝ22 |
| 37.1 | ﾎﾞｽ - 押し出し22 | ICE / Boss | False | a2, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 37.1.1 | a2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 37.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ12 | CurvePattern | False | ﾎﾞｽ - 押し出し22, a2 |
| 37.3 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ22 | CurvePattern | True | ﾎﾞｽ - 押し出し22, a2 |
| 38 | a3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 39 | ﾎﾞｽ - 押し出し23 | ICE / Boss | False | a3, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 39.1 | a3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 40 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ13 | CurvePattern | False | ﾎﾞｽ - 押し出し23, a3 |
| 41 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ23 | CurvePattern | False | ﾎﾞｽ - 押し出し23, a3 |
| 42 | スタッド7.5<4> | LibraryFeature | False | a3, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾎﾞｽ - 押し出し23, ｶｰﾌﾞ ﾊﾟﾀｰﾝ13, ｶｰﾌﾞ ﾊﾟﾀｰﾝ23 |
| 42.1 | ﾎﾞｽ - 押し出し23 | ICE / Boss | False | a3, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 42.1.1 | a3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 42.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ13 | CurvePattern | False | ﾎﾞｽ - 押し出し23, a3 |
| 42.3 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ23 | CurvePattern | False | ﾎﾞｽ - 押し出し23, a3 |
| 43 | Sheet<2> | CutListFolder | False |  |
| 44 | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 | FlatPattern | True | 板金, 板金19, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 44.1 | ﾍﾞﾝﾄﾞ-ﾗｲﾝ2 | ProfileFeature | False |  |
| 44.2 | 境界ﾎﾞｯｸｽ2 | ProfileFeature | True |  |
| 44.3 | ｽｹｯﾁ変換1 | FoldUpDownSketchFolder | True |  |

## 1618-04-bh-A6-fr2.SLDPRT

Configuration: `Default`. Top-level nodes: 34. Evidence JSON: `evidence/api/1618-04-bh-A6-fr2.SLDPRT.json`.

| API path | Name | GetTypeName2 / legacy | Suppressed active | Parents |
| --- | --- | --- | --- | --- |
| 1 | Configuration Comments | ConfigCommentsFolder | False |  |
| 2 | Favorites | FavoriteFolder | False |  |
| 3 | History | HistoryFolder | False |  |
| 4 | Selection Sets | SelectionSetFolder | False |  |
| 5 | Sensors | SensorFolder | False |  |
| 6 | ﾃﾞｻﾞｲﾝ ﾊﾞｲﾝﾀﾞｰ | DocsFolder | False |  |
| 7 | Annotations | DetailCabinet | False |  |
| 7.1 | Notes | NotesAreaFtrFolder | False |  |
| 7.2 | Notes Area | AnnotationViewFeat | False |  |
| 7.3 | Notes1___EndTag___ | FtrFolder | False |  |
| 8 | Markups | InkMarkupFolder | False |  |
| 9 | Lights and Cameras | EnvFolder | False |  |
| 9.1 | Ambient | AmbientLight | False |  |
| 9.2 | Directional1 | DirectionLight | False |  |
| 10 | Solid Bodies | SolidBodyFolder | False |  |
| 10.1 | Sheet<1> | CutListFolder | False |  |
| 11 | Surface Bodies | SurfaceBodyFolder | False |  |
| 12 | Comments | CommentsFolder | False |  |
| 13 | Equations | EqnFolder | False |  |
| 14 | A1100- | MaterialFolder | False |  |
| 15 | Front | RefPlane | False |  |
| 16 | Top | RefPlane | False |  |
| 17 | Right | RefPlane | False |  |
| 18 | Origin | OriginProfileFeature | False | Front |
| 19 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 20 | 板金19 | SheetMetal | False | 板金 |
| 21 | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 | SMBaseFlange | False | 板金, 板金19, ｽｹｯﾁ1, Right |
| 21.1 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 21.2 | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.3 | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 22 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 23 | ｶｯﾄ - 押し出し14 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 23.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 24 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し14, a |
| 25 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | False | ｶｯﾄ - 押し出し14, a |
| 26 | スタッド10x16<1> | LibraryFeature | True | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し14, ｶｰﾌﾞ ﾊﾟﾀｰﾝ1, ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 |
| 26.1 | ｶｯﾄ - 押し出し14 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 26.1.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 26.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し14, a |
| 26.3 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | False | ｶｯﾄ - 押し出し14, a |
| 27 | ｽｹｯﾁ4 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 28 | ｶｯﾄ - 押し出し3 | Cut | False | ｽｹｯﾁ4, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 28.1 | ｽｹｯﾁ4 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 29 | Sheet<1> | CutListFolder | False |  |
| 30 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 31 | ｶｯﾄ - 押し出し1 | Cut | False | ｽｹｯﾁ2, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 31.1 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 32 | ｽｹｯﾁ7 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 33 | ｶｯﾄ - 押し出し15 | Cut | False | ｽｹｯﾁ7, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 33.1 | ｽｹｯﾁ7 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 34 | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 | FlatPattern | True | 板金, 板金19, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 34.1 | ﾍﾞﾝﾄﾞ-ﾗｲﾝ2 | ProfileFeature | False |  |
| 34.2 | 境界ﾎﾞｯｸｽ2 | ProfileFeature | True |  |
| 34.3 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ1>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 34.4 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ3>2 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 34.5 | ｽｹｯﾁ変換1 | FoldUpDownSketchFolder | True |  |

## 1618-04-bh-A6-fr3.SLDPRT

Configuration: `Default`. Top-level nodes: 45. Evidence JSON: `evidence/api/1618-04-bh-A6-fr3.SLDPRT.json`.

| API path | Name | GetTypeName2 / legacy | Suppressed active | Parents |
| --- | --- | --- | --- | --- |
| 1 | Configuration Comments | ConfigCommentsFolder | False |  |
| 2 | Favorites | FavoriteFolder | False |  |
| 3 | History | HistoryFolder | False |  |
| 4 | Selection Sets | SelectionSetFolder | False |  |
| 5 | Sensors | SensorFolder | False |  |
| 6 | ﾃﾞｻﾞｲﾝ ﾊﾞｲﾝﾀﾞｰ | DocsFolder | False |  |
| 7 | Annotations | DetailCabinet | False |  |
| 7.1 | Notes | NotesAreaFtrFolder | False |  |
| 7.2 | Notes Area | AnnotationViewFeat | False |  |
| 7.3 | Notes1___EndTag___ | FtrFolder | False |  |
| 8 | Markups | InkMarkupFolder | False |  |
| 9 | Lights and Cameras | EnvFolder | False |  |
| 9.1 | Ambient | AmbientLight | False |  |
| 9.2 | Directional1 | DirectionLight | False |  |
| 10 | Solid Bodies | SolidBodyFolder | False |  |
| 10.1 | Sheet<1> | CutListFolder | False |  |
| 10.2 | Sheet<2> | CutListFolder | False |  |
| 11 | Surface Bodies | SurfaceBodyFolder | False |  |
| 12 | Comments | CommentsFolder | False |  |
| 13 | Equations | EqnFolder | False |  |
| 14 | A1100- | MaterialFolder | False |  |
| 15 | Front | RefPlane | False |  |
| 16 | Top | RefPlane | False |  |
| 17 | Right | RefPlane | False |  |
| 18 | Origin | OriginProfileFeature | False | Front |
| 19 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 20 | 板金19 | SheetMetal | False | 板金 |
| 21 | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 | SMBaseFlange | False | 板金, 板金19, ｽｹｯﾁ1, Right |
| 21.1 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 21.2 | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.3 | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.4 | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.5 | ﾍﾞｰｽﾍﾞﾝﾄﾞ5 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.6 | ﾍﾞｰｽﾍﾞﾝﾄﾞ6 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 22 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 23 | ｶｯﾄ - 押し出し110 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 23.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 24 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し110, a |
| 25 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | True | ｶｯﾄ - 押し出し110, a |
| 26 | スタッド10x16<1> | LibraryFeature | True | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し110, ｶｰﾌﾞ ﾊﾟﾀｰﾝ1, ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 |
| 26.1 | ｶｯﾄ - 押し出し110 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 26.1.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 26.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し110, a |
| 26.3 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | True | ｶｯﾄ - 押し出し110, a |
| 27 | Sheet<1> | CutListFolder | False |  |
| 28 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ6 |
| 29 | ｶｯﾄ - 押し出し1 | Cut | False | ｽｹｯﾁ2, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ6, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 29.1 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ6 |
| 30 | ｽｹｯﾁ27 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 31 | ｶｯﾄ - 押し出し116 | Cut | False | ｽｹｯﾁ27, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 31.1 | ｽｹｯﾁ27 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 32 | ｽｹｯﾁ22 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ｶｯﾄ - 押し出し116 |
| 33 | ｶｯﾄ - 押し出し114 | Cut | False | ｽｹｯﾁ22, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ｶｯﾄ - 押し出し116 |
| 33.1 | ｽｹｯﾁ22 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ｶｯﾄ - 押し出し116 |
| 34 | Sheet<2> | CutListFolder | False |  |
| 35 | ｽｹｯﾁ23 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 36 | ｶｯﾄ - 押し出し115 | Cut | False | ｽｹｯﾁ23, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ6, ｶｯﾄ - 押し出し1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 36.1 | ｽｹｯﾁ23 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 37 | ｽｹｯﾁ28 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 38 | ｶｯﾄ - 押し出し117 | Cut | False | ｽｹｯﾁ28, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 38.1 | ｽｹｯﾁ28 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 39 | ｽｹｯﾁ29 | ProfileFeature | False | ｶｯﾄ - 押し出し116, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 40 | ﾎﾞｽ - 押し出し1 | ICE / Boss | False | ｽｹｯﾁ29, ｶｯﾄ - 押し出し116, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 40.1 | ｽｹｯﾁ29 | ProfileFeature | False | ｶｯﾄ - 押し出し116, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 41 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 42 | ｶｯﾄ - 押し出し119 | ICE / Cut | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 42.1 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 43 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern | False | ｶｯﾄ - 押し出し119, a1 |
| 44 | 4.2<1> | LibraryFeature | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し119, ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 |
| 44.1 | ｶｯﾄ - 押し出し119 | ICE / Cut | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 44.1.1 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 44.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern | False | ｶｯﾄ - 押し出し119, a1 |
| 45 | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 | FlatPattern | True | 板金, 板金19, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 45.1 | ﾍﾞﾝﾄﾞ-ﾗｲﾝ2 | ProfileFeature | False |  |
| 45.2 | 境界ﾎﾞｯｸｽ2 | ProfileFeature | True |  |
| 45.3 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ1>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 45.4 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ3>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 45.5 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ4>4 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 45.6 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ5>4 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ5 |
| 45.7 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ6>4 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ6 |
| 45.8 | ｽｹｯﾁ変換1 | FoldUpDownSketchFolder | True |  |

## 1618-04-bh-A6-fr4.SLDPRT

Configuration: `Default`. Top-level nodes: 36. Evidence JSON: `evidence/api/1618-04-bh-A6-fr4.SLDPRT.json`.

| API path | Name | GetTypeName2 / legacy | Suppressed active | Parents |
| --- | --- | --- | --- | --- |
| 1 | Configuration Comments | ConfigCommentsFolder | False |  |
| 2 | Favorites | FavoriteFolder | False |  |
| 3 | History | HistoryFolder | False |  |
| 4 | Selection Sets | SelectionSetFolder | False |  |
| 5 | Sensors | SensorFolder | False |  |
| 6 | ﾃﾞｻﾞｲﾝ ﾊﾞｲﾝﾀﾞｰ | DocsFolder | False |  |
| 7 | Annotations | DetailCabinet | False |  |
| 7.1 | Notes | NotesAreaFtrFolder | False |  |
| 7.2 | Notes Area | AnnotationViewFeat | False |  |
| 7.3 | Notes1___EndTag___ | FtrFolder | False |  |
| 8 | Markups | InkMarkupFolder | False |  |
| 9 | Lights and Cameras | EnvFolder | False |  |
| 9.1 | Ambient | AmbientLight | False |  |
| 9.2 | Directional1 | DirectionLight | False |  |
| 10 | Solid Bodies | SolidBodyFolder | False |  |
| 10.1 | Sheet<1> | CutListFolder | False |  |
| 11 | Surface Bodies | SurfaceBodyFolder | False |  |
| 12 | Comments | CommentsFolder | False |  |
| 13 | Equations | EqnFolder | False |  |
| 14 | A1100- | MaterialFolder | False |  |
| 15 | Front | RefPlane | False |  |
| 16 | Top | RefPlane | False |  |
| 17 | Right | RefPlane | False |  |
| 18 | Origin | OriginProfileFeature | False | Front |
| 19 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 20 | 板金19 | SheetMetal | False | 板金 |
| 21 | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 | SMBaseFlange | False | 板金, 板金19, ｽｹｯﾁ1, Right |
| 21.1 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 21.2 | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.3 | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.4 | ﾍﾞｰｽﾍﾞﾝﾄﾞ5 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 22 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 23 | ｶｯﾄ - 押し出し14 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 23.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 24 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し14, a |
| 25 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | True | ｶｯﾄ - 押し出し14, a |
| 26 | スタッド10x16<1> | LibraryFeature | True | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し14, ｶｰﾌﾞ ﾊﾟﾀｰﾝ1, ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 |
| 26.1 | ｶｯﾄ - 押し出し14 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 26.1.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 26.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し14, a |
| 26.3 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | True | ｶｯﾄ - 押し出し14, a |
| 27 | ｽｹｯﾁ4 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ5, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 28 | ｶｯﾄ - 押し出し3 | Cut | False | ｽｹｯﾁ4, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ5, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 28.1 | ｽｹｯﾁ4 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ5, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 29 | Sheet<1> | CutListFolder | False |  |
| 30 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ5, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 31 | ｶｯﾄ - 押し出し1 | Cut | False | ｽｹｯﾁ2, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ5, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 31.1 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ5, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 32 | ｽｹｯﾁ5 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ5, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ｶｯﾄ - 押し出し3 |
| 33 | ｶｯﾄ - 押し出し15 | Cut | False | ｽｹｯﾁ5, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ5, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ｶｯﾄ - 押し出し3 |
| 33.1 | ｽｹｯﾁ5 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ5, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ｶｯﾄ - 押し出し3 |
| 34 | ｽｹｯﾁ6 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ5, ｶｯﾄ - 押し出し1 |
| 35 | ｶｯﾄ - 押し出し16 | Cut | False | ｽｹｯﾁ6, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ5, ｶｯﾄ - 押し出し1 |
| 35.1 | ｽｹｯﾁ6 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ5, ｶｯﾄ - 押し出し1 |
| 36 | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 | FlatPattern | True | 板金, 板金19, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 36.1 | ﾍﾞﾝﾄﾞ-ﾗｲﾝ2 | ProfileFeature | False |  |
| 36.2 | 境界ﾎﾞｯｸｽ2 | ProfileFeature | True |  |
| 36.3 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ3>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 36.4 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ5>6 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ5 |
| 36.5 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ1>2 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 36.6 | ｽｹｯﾁ変換1 | FoldUpDownSketchFolder | True |  |

## 1618-04-bh-A6-中骨1.SLDPRT

Configuration: `Default`. Top-level nodes: 25. Evidence JSON: `evidence/api/1618-04-bh-A6-中骨1.SLDPRT.json`.

| API path | Name | GetTypeName2 / legacy | Suppressed active | Parents |
| --- | --- | --- | --- | --- |
| 1 | Configuration Comments | ConfigCommentsFolder | False |  |
| 2 | Favorites | FavoriteFolder | False |  |
| 3 | History | HistoryFolder | False |  |
| 4 | Selection Sets | SelectionSetFolder | False |  |
| 5 | Sensors | SensorFolder | False |  |
| 6 | ﾃﾞｻﾞｲﾝ ﾊﾞｲﾝﾀﾞｰ | DocsFolder | False |  |
| 7 | Annotations | DetailCabinet | False |  |
| 7.1 | Notes | NotesAreaFtrFolder | False |  |
| 7.2 | Notes Area | AnnotationViewFeat | False |  |
| 7.3 | Notes1___EndTag___ | FtrFolder | False |  |
| 8 | Markups | InkMarkupFolder | False |  |
| 9 | Lights and Cameras | EnvFolder | False |  |
| 9.1 | Ambient | AmbientLight | False |  |
| 9.2 | Directional1 | DirectionLight | False |  |
| 10 | Solid Bodies | SolidBodyFolder | False |  |
| 10.1 | Sheet<1> | CutListFolder | False |  |
| 11 | Surface Bodies | SurfaceBodyFolder | False |  |
| 12 | Comments | CommentsFolder | False |  |
| 13 | Equations | EqnFolder | False |  |
| 14 | A1100- | MaterialFolder | False |  |
| 15 | Front | RefPlane | False |  |
| 16 | Top | RefPlane | False |  |
| 17 | Right | RefPlane | False |  |
| 18 | Origin | OriginProfileFeature | False | Front |
| 19 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 20 | 板金19 | SheetMetal | False | 板金 |
| 21 | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 | SMBaseFlange | False | 板金, 板金19, ｽｹｯﾁ1, Right |
| 21.1 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 21.2 | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.3 | ﾍﾞｰｽﾍﾞﾝﾄﾞ2 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.4 | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.5 | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 22 | Sheet<1> | CutListFolder | False |  |
| 23 | ｽｹｯﾁ6 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 24 | ｶｯﾄ - 押し出し13 | Cut | False | ｽｹｯﾁ6, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 24.1 | ｽｹｯﾁ6 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 25 | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 | FlatPattern | True | 板金, 板金19, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 25.1 | ﾍﾞﾝﾄﾞ-ﾗｲﾝ2 | ProfileFeature | False |  |
| 25.2 | 境界ﾎﾞｯｸｽ2 | ProfileFeature | True |  |
| 25.3 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ1>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 25.4 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ2>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ2 |
| 25.5 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ3>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 25.6 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ4>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 25.7 | ｽｹｯﾁ変換1 | FoldUpDownSketchFolder | True |  |

## 3615-04-bh-T1-1.SLDPRT

Configuration: `Default`. Top-level nodes: 23. Evidence JSON: `evidence/api/3615-04-bh-T1-1.SLDPRT.json`.

| API path | Name | GetTypeName2 / legacy | Suppressed active | Parents |
| --- | --- | --- | --- | --- |
| 1 | Configuration Comments | ConfigCommentsFolder | False |  |
| 2 | Favorites | FavoriteFolder | False |  |
| 3 | History | HistoryFolder | False |  |
| 4 | Selection Sets | SelectionSetFolder | False |  |
| 5 | Sensors | SensorFolder | False |  |
| 6 | ﾃﾞｻﾞｲﾝ ﾊﾞｲﾝﾀﾞｰ | DocsFolder | False |  |
| 7 | Annotations | DetailCabinet | False |  |
| 7.1 | Notes | NotesAreaFtrFolder | False |  |
| 7.2 | Notes Area | AnnotationViewFeat | False |  |
| 7.3 | Notes1___EndTag___ | FtrFolder | False |  |
| 8 | Markups | InkMarkupFolder | False |  |
| 9 | Lights and Cameras | EnvFolder | False |  |
| 9.1 | Ambient | AmbientLight | False |  |
| 9.2 | Directional1 | DirectionLight | False |  |
| 10 | Solid Bodies | SolidBodyFolder | False |  |
| 10.1 | Sheet<1> | CutListFolder | False |  |
| 11 | Surface Bodies | SurfaceBodyFolder | False |  |
| 12 | Comments | CommentsFolder | False |  |
| 13 | Equations | EqnFolder | False |  |
| 14 | A1100P- | MaterialFolder | False |  |
| 15 | Front | RefPlane | False |  |
| 16 | Top | RefPlane | False |  |
| 17 | Right | RefPlane | False |  |
| 18 | Origin | OriginProfileFeature | False | Front |
| 19 | ｽｹｯﾁ1 | ProfileFeature | False | Front, Origin |
| 20 | 板金19 | SheetMetal | False | 板金 |
| 21 | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 | SMBaseFlange | False | 板金, 板金19, ｽｹｯﾁ1, Front |
| 21.1 | ｽｹｯﾁ1 | ProfileFeature | False | Front, Origin |
| 22 | Sheet<1> | CutListFolder | False |  |
| 23 | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 | FlatPattern | True | 板金, 板金19, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 23.1 | ﾍﾞﾝﾄﾞ-ﾗｲﾝ2 | ProfileFeature | False |  |
| 23.2 | 境界ﾎﾞｯｸｽ2 | ProfileFeature | True |  |
| 23.3 | ｽｹｯﾁ変換1 | FoldUpDownSketchFolder | True |  |
| 23.3.1 | ｽｹｯﾁ2-2 | ProfileFeature | True | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 |

## 3615-04-bh-T1-fr1.SLDPRT

Configuration: `Default`. Top-level nodes: 36. Evidence JSON: `evidence/api/3615-04-bh-T1-fr1.SLDPRT.json`.

| API path | Name | GetTypeName2 / legacy | Suppressed active | Parents |
| --- | --- | --- | --- | --- |
| 1 | Configuration Comments | ConfigCommentsFolder | False |  |
| 2 | Favorites | FavoriteFolder | False |  |
| 3 | History | HistoryFolder | False |  |
| 4 | Selection Sets | SelectionSetFolder | False |  |
| 5 | Sensors | SensorFolder | False |  |
| 6 | ﾃﾞｻﾞｲﾝ ﾊﾞｲﾝﾀﾞｰ | DocsFolder | False |  |
| 7 | Annotations | DetailCabinet | False |  |
| 7.1 | Notes | NotesAreaFtrFolder | False |  |
| 7.2 | Notes Area | AnnotationViewFeat | False |  |
| 7.3 | Notes1___EndTag___ | FtrFolder | False |  |
| 8 | Markups | InkMarkupFolder | False |  |
| 9 | Lights and Cameras | EnvFolder | False |  |
| 9.1 | Ambient | AmbientLight | False |  |
| 9.2 | Directional1 | DirectionLight | False |  |
| 10 | Solid Bodies | SolidBodyFolder | False |  |
| 10.1 | Sheet<1> | CutListFolder | False |  |
| 11 | Surface Bodies | SurfaceBodyFolder | False |  |
| 12 | Comments | CommentsFolder | False |  |
| 13 | Equations | EqnFolder | False |  |
| 14 | A1100P- | MaterialFolder | False |  |
| 15 | Front | RefPlane | False |  |
| 16 | Top | RefPlane | False |  |
| 17 | Right | RefPlane | False |  |
| 18 | Origin | OriginProfileFeature | False | Front |
| 19 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 20 | 板金19 | SheetMetal | False | 板金 |
| 21 | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 | SMBaseFlange | False | 板金, 板金19, ｽｹｯﾁ1, Right |
| 21.1 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 21.2 | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.3 | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.4 | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 22 | Sheet<1> | CutListFolder | False |  |
| 23 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 24 | ｶｯﾄ - 押し出し1 | Cut | False | ｽｹｯﾁ2, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 24.1 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 25 | ｽｹｯﾁ3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 26 | ｶｯﾄ - 押し出し2 | Cut | False | ｽｹｯﾁ3, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 26.1 | ｽｹｯﾁ3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 27 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 28 | ｶｯﾄ - 押し出し11 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 28.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 29 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 30 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 31 | スタッド10x16<1> | LibraryFeature | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し11, ｶｰﾌﾞ ﾊﾟﾀｰﾝ1, ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 |
| 31.1 | ｶｯﾄ - 押し出し11 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 31.1.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 31.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 31.3 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 32 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 33 | ｶｯﾄ - 押し出し13 | ICE / Cut | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 33.1 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 34 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern | False | ｶｯﾄ - 押し出し13, a1 |
| 35 | 4.2<1> | LibraryFeature | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し13, ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 |
| 35.1 | ｶｯﾄ - 押し出し13 | ICE / Cut | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 35.1.1 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 35.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern | False | ｶｯﾄ - 押し出し13, a1 |
| 36 | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 | FlatPattern | True | 板金, 板金19, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｽｹｯﾁ2 |
| 36.1 | ﾍﾞﾝﾄﾞ-ﾗｲﾝ2 | ProfileFeature | False |  |
| 36.2 | 境界ﾎﾞｯｸｽ2 | ProfileFeature | True |  |
| 36.3 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ1>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 36.4 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ3>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 36.5 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ4>4 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 36.6 | ｽｹｯﾁ変換1 | FoldUpDownSketchFolder | True |  |
| 36.6.1 | ｽｹｯﾁ2-1 | ProfileFeature | False | ｽｹｯﾁ2, ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 |

## 3615-04-bh-T1-fr2.SLDPRT

Configuration: `Default`. Top-level nodes: 32. Evidence JSON: `evidence/api/3615-04-bh-T1-fr2.SLDPRT.json`.

| API path | Name | GetTypeName2 / legacy | Suppressed active | Parents |
| --- | --- | --- | --- | --- |
| 1 | Configuration Comments | ConfigCommentsFolder | False |  |
| 2 | Favorites | FavoriteFolder | False |  |
| 3 | History | HistoryFolder | False |  |
| 4 | Selection Sets | SelectionSetFolder | False |  |
| 5 | Sensors | SensorFolder | False |  |
| 6 | ﾃﾞｻﾞｲﾝ ﾊﾞｲﾝﾀﾞｰ | DocsFolder | False |  |
| 7 | Annotations | DetailCabinet | False |  |
| 7.1 | Notes | NotesAreaFtrFolder | False |  |
| 7.2 | Notes Area | AnnotationViewFeat | False |  |
| 7.3 | Notes1___EndTag___ | FtrFolder | False |  |
| 8 | Markups | InkMarkupFolder | False |  |
| 9 | Lights and Cameras | EnvFolder | False |  |
| 9.1 | Ambient | AmbientLight | False |  |
| 9.2 | Directional1 | DirectionLight | False |  |
| 10 | Solid Bodies | SolidBodyFolder | False |  |
| 10.1 | Sheet<1> | CutListFolder | False |  |
| 11 | Surface Bodies | SurfaceBodyFolder | False |  |
| 12 | Comments | CommentsFolder | False |  |
| 13 | Equations | EqnFolder | False |  |
| 14 | A1100P- | MaterialFolder | False |  |
| 15 | Front | RefPlane | False |  |
| 16 | Top | RefPlane | False |  |
| 17 | Right | RefPlane | False |  |
| 18 | Origin | OriginProfileFeature | False | Front |
| 19 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 20 | 板金19 | SheetMetal | False | 板金 |
| 21 | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 | SMBaseFlange | False | 板金, 板金19, ｽｹｯﾁ1, Right |
| 21.1 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 21.2 | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.3 | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.4 | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 22 | Sheet<1> | CutListFolder | False |  |
| 23 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 24 | ｶｯﾄ - 押し出し1 | Cut | False | ｽｹｯﾁ2, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 24.1 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 25 | ｽｹｯﾁ3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 26 | ｶｯﾄ - 押し出し2 | Cut | False | ｽｹｯﾁ3, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 26.1 | ｽｹｯﾁ3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 27 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 28 | ｶｯﾄ - 押し出し11 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 28.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 29 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 30 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 31 | スタッド10x16<1> | LibraryFeature | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し11, ｶｰﾌﾞ ﾊﾟﾀｰﾝ1, ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 |
| 31.1 | ｶｯﾄ - 押し出し11 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 31.1.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 31.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 31.3 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 32 | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 | FlatPattern | True | 板金, 板金19, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 32.1 | ﾍﾞﾝﾄﾞ-ﾗｲﾝ2 | ProfileFeature | False |  |
| 32.2 | 境界ﾎﾞｯｸｽ2 | ProfileFeature | True |  |
| 32.3 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ1>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 32.4 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ3>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 32.5 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ4>9 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 32.6 | ｽｹｯﾁ変換1 | FoldUpDownSketchFolder | True |  |

## 3615-04-bh-T1-fr3.SLDPRT

Configuration: `Default`. Top-level nodes: 36. Evidence JSON: `evidence/api/3615-04-bh-T1-fr3.SLDPRT.json`.

| API path | Name | GetTypeName2 / legacy | Suppressed active | Parents |
| --- | --- | --- | --- | --- |
| 1 | Configuration Comments | ConfigCommentsFolder | False |  |
| 2 | Favorites | FavoriteFolder | False |  |
| 3 | History | HistoryFolder | False |  |
| 4 | Selection Sets | SelectionSetFolder | False |  |
| 5 | Sensors | SensorFolder | False |  |
| 6 | ﾃﾞｻﾞｲﾝ ﾊﾞｲﾝﾀﾞｰ | DocsFolder | False |  |
| 7 | Annotations | DetailCabinet | False |  |
| 7.1 | Notes | NotesAreaFtrFolder | False |  |
| 7.2 | Notes Area | AnnotationViewFeat | False |  |
| 7.3 | Notes1___EndTag___ | FtrFolder | False |  |
| 8 | Markups | InkMarkupFolder | False |  |
| 9 | Lights and Cameras | EnvFolder | False |  |
| 9.1 | Ambient | AmbientLight | False |  |
| 9.2 | Directional1 | DirectionLight | False |  |
| 10 | Solid Bodies | SolidBodyFolder | False |  |
| 10.1 | Sheet<1> | CutListFolder | False |  |
| 11 | Surface Bodies | SurfaceBodyFolder | False |  |
| 12 | Comments | CommentsFolder | False |  |
| 13 | Equations | EqnFolder | False |  |
| 14 | A1100P- | MaterialFolder | False |  |
| 15 | Front | RefPlane | False |  |
| 16 | Top | RefPlane | False |  |
| 17 | Right | RefPlane | False |  |
| 18 | Origin | OriginProfileFeature | False | Front |
| 19 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 20 | 板金19 | SheetMetal | False | 板金 |
| 21 | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 | SMBaseFlange | False | 板金, 板金19, ｽｹｯﾁ1, Right |
| 21.1 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 21.2 | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.3 | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.4 | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 22 | Sheet<1> | CutListFolder | False |  |
| 23 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 24 | ｶｯﾄ - 押し出し1 | Cut | False | ｽｹｯﾁ2, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 24.1 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 25 | ｽｹｯﾁ3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 26 | ｶｯﾄ - 押し出し2 | Cut | False | ｽｹｯﾁ3, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 26.1 | ｽｹｯﾁ3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 27 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 28 | ｶｯﾄ - 押し出し11 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 28.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 29 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 30 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 31 | スタッド10x16<1> | LibraryFeature | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し11, ｶｰﾌﾞ ﾊﾟﾀｰﾝ1, ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 |
| 31.1 | ｶｯﾄ - 押し出し11 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 31.1.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 31.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 31.3 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 32 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 33 | ｶｯﾄ - 押し出し13 | ICE / Cut | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 33.1 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 34 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern | False | ｶｯﾄ - 押し出し13, a1 |
| 35 | 4.2<1> | LibraryFeature | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し13, ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 |
| 35.1 | ｶｯﾄ - 押し出し13 | ICE / Cut | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 35.1.1 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 35.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern | False | ｶｯﾄ - 押し出し13, a1 |
| 36 | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 | FlatPattern | True | 板金, 板金19, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 36.1 | ﾍﾞﾝﾄﾞ-ﾗｲﾝ2 | ProfileFeature | False |  |
| 36.2 | 境界ﾎﾞｯｸｽ2 | ProfileFeature | True |  |
| 36.3 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ1>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 36.4 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ3>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 36.5 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ4>4 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 36.6 | ｽｹｯﾁ変換1 | FoldUpDownSketchFolder | True |  |

## 3615-04-bh-T1-fr4.SLDPRT

Configuration: `Default`. Top-level nodes: 32. Evidence JSON: `evidence/api/3615-04-bh-T1-fr4.SLDPRT.json`.

| API path | Name | GetTypeName2 / legacy | Suppressed active | Parents |
| --- | --- | --- | --- | --- |
| 1 | Configuration Comments | ConfigCommentsFolder | False |  |
| 2 | Favorites | FavoriteFolder | False |  |
| 3 | History | HistoryFolder | False |  |
| 4 | Selection Sets | SelectionSetFolder | False |  |
| 5 | Sensors | SensorFolder | False |  |
| 6 | ﾃﾞｻﾞｲﾝ ﾊﾞｲﾝﾀﾞｰ | DocsFolder | False |  |
| 7 | Annotations | DetailCabinet | False |  |
| 7.1 | Notes | NotesAreaFtrFolder | False |  |
| 7.2 | Notes Area | AnnotationViewFeat | False |  |
| 7.3 | Notes1___EndTag___ | FtrFolder | False |  |
| 8 | Markups | InkMarkupFolder | False |  |
| 9 | Lights and Cameras | EnvFolder | False |  |
| 9.1 | Ambient | AmbientLight | False |  |
| 9.2 | Directional1 | DirectionLight | False |  |
| 10 | Solid Bodies | SolidBodyFolder | False |  |
| 10.1 | Sheet<1> | CutListFolder | False |  |
| 11 | Surface Bodies | SurfaceBodyFolder | False |  |
| 12 | Comments | CommentsFolder | False |  |
| 13 | Equations | EqnFolder | False |  |
| 14 | A1100P- | MaterialFolder | False |  |
| 15 | Front | RefPlane | False |  |
| 16 | Top | RefPlane | False |  |
| 17 | Right | RefPlane | False |  |
| 18 | Origin | OriginProfileFeature | False | Front |
| 19 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 20 | 板金19 | SheetMetal | False | 板金 |
| 21 | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 | SMBaseFlange | False | 板金, 板金19, ｽｹｯﾁ1, Right |
| 21.1 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 21.2 | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.3 | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 22 | Sheet<1> | CutListFolder | False |  |
| 23 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 24 | ｶｯﾄ - 押し出し1 | Cut | False | ｽｹｯﾁ2, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 24.1 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 25 | ｽｹｯﾁ3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 26 | ｶｯﾄ - 押し出し2 | Cut | False | ｽｹｯﾁ3, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 26.1 | ｽｹｯﾁ3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 27 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 28 | ｶｯﾄ - 押し出し11 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 28.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 29 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 30 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 31 | スタッド10x16<1> | LibraryFeature | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し11, ｶｰﾌﾞ ﾊﾟﾀｰﾝ1, ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 |
| 31.1 | ｶｯﾄ - 押し出し11 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 31.1.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 31.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 31.3 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 32 | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 | FlatPattern | True | 板金, 板金19, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 32.1 | ﾍﾞﾝﾄﾞ-ﾗｲﾝ2 | ProfileFeature | False |  |
| 32.2 | 境界ﾎﾞｯｸｽ2 | ProfileFeature | True |  |
| 32.3 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ1>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 32.4 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ3>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 32.5 | ｽｹｯﾁ変換1 | FoldUpDownSketchFolder | True |  |

## 3615-04-bh-T2-1.SLDPRT

Configuration: `Default`. Top-level nodes: 23. Evidence JSON: `evidence/api/3615-04-bh-T2-1.SLDPRT.json`.

| API path | Name | GetTypeName2 / legacy | Suppressed active | Parents |
| --- | --- | --- | --- | --- |
| 1 | Configuration Comments | ConfigCommentsFolder | False |  |
| 2 | Favorites | FavoriteFolder | False |  |
| 3 | History | HistoryFolder | False |  |
| 4 | Selection Sets | SelectionSetFolder | False |  |
| 5 | Sensors | SensorFolder | False |  |
| 6 | ﾃﾞｻﾞｲﾝ ﾊﾞｲﾝﾀﾞｰ | DocsFolder | False |  |
| 7 | Annotations | DetailCabinet | False |  |
| 7.1 | Notes | NotesAreaFtrFolder | False |  |
| 7.2 | Notes Area | AnnotationViewFeat | False |  |
| 7.3 | Notes1___EndTag___ | FtrFolder | False |  |
| 8 | Markups | InkMarkupFolder | False |  |
| 9 | Lights and Cameras | EnvFolder | False |  |
| 9.1 | Ambient | AmbientLight | False |  |
| 9.2 | Directional1 | DirectionLight | False |  |
| 10 | Solid Bodies | SolidBodyFolder | False |  |
| 10.1 | Sheet<1> | CutListFolder | False |  |
| 11 | Surface Bodies | SurfaceBodyFolder | False |  |
| 12 | Comments | CommentsFolder | False |  |
| 13 | Equations | EqnFolder | False |  |
| 14 | A1100P- | MaterialFolder | False |  |
| 15 | Front | RefPlane | False |  |
| 16 | Top | RefPlane | False |  |
| 17 | Right | RefPlane | False |  |
| 18 | Origin | OriginProfileFeature | False | Front |
| 19 | ｽｹｯﾁ1 | ProfileFeature | False | Front, Origin |
| 20 | 板金19 | SheetMetal | False | 板金 |
| 21 | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 | SMBaseFlange | False | 板金, 板金19, ｽｹｯﾁ1, Front |
| 21.1 | ｽｹｯﾁ1 | ProfileFeature | False | Front, Origin |
| 22 | Sheet<1> | CutListFolder | False |  |
| 23 | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 | FlatPattern | True | 板金, 板金19, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 23.1 | ﾍﾞﾝﾄﾞ-ﾗｲﾝ2 | ProfileFeature | False |  |
| 23.2 | 境界ﾎﾞｯｸｽ2 | ProfileFeature | True |  |
| 23.3 | ｽｹｯﾁ変換1 | FoldUpDownSketchFolder | True |  |

## 3615-04-bh-T2-fr1.SLDPRT

Configuration: `Default`. Top-level nodes: 36. Evidence JSON: `evidence/api/3615-04-bh-T2-fr1.SLDPRT.json`.

| API path | Name | GetTypeName2 / legacy | Suppressed active | Parents |
| --- | --- | --- | --- | --- |
| 1 | Configuration Comments | ConfigCommentsFolder | False |  |
| 2 | Favorites | FavoriteFolder | False |  |
| 3 | History | HistoryFolder | False |  |
| 4 | Selection Sets | SelectionSetFolder | False |  |
| 5 | Sensors | SensorFolder | False |  |
| 6 | ﾃﾞｻﾞｲﾝ ﾊﾞｲﾝﾀﾞｰ | DocsFolder | False |  |
| 7 | Annotations | DetailCabinet | False |  |
| 7.1 | Notes | NotesAreaFtrFolder | False |  |
| 7.2 | Notes Area | AnnotationViewFeat | False |  |
| 7.3 | Notes1___EndTag___ | FtrFolder | False |  |
| 8 | Markups | InkMarkupFolder | False |  |
| 9 | Lights and Cameras | EnvFolder | False |  |
| 9.1 | Ambient | AmbientLight | False |  |
| 9.2 | Directional1 | DirectionLight | False |  |
| 10 | Solid Bodies | SolidBodyFolder | False |  |
| 10.1 | Sheet<1> | CutListFolder | False |  |
| 11 | Surface Bodies | SurfaceBodyFolder | False |  |
| 12 | Comments | CommentsFolder | False |  |
| 13 | Equations | EqnFolder | False |  |
| 14 | A1100P- | MaterialFolder | False |  |
| 15 | Front | RefPlane | False |  |
| 16 | Top | RefPlane | False |  |
| 17 | Right | RefPlane | False |  |
| 18 | Origin | OriginProfileFeature | False | Front |
| 19 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 20 | 板金19 | SheetMetal | False | 板金 |
| 21 | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 | SMBaseFlange | False | 板金, 板金19, ｽｹｯﾁ1, Right |
| 21.1 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 21.2 | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.3 | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.4 | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 22 | Sheet<1> | CutListFolder | False |  |
| 23 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 24 | ｶｯﾄ - 押し出し1 | Cut | False | ｽｹｯﾁ2, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 24.1 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 25 | ｽｹｯﾁ3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 26 | ｶｯﾄ - 押し出し2 | Cut | False | ｽｹｯﾁ3, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 26.1 | ｽｹｯﾁ3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 27 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 28 | ｶｯﾄ - 押し出し11 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 28.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 29 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 30 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 31 | スタッド10x16<1> | LibraryFeature | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し11, ｶｰﾌﾞ ﾊﾟﾀｰﾝ1, ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 |
| 31.1 | ｶｯﾄ - 押し出し11 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 31.1.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 31.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 31.3 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 32 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 33 | ｶｯﾄ - 押し出し13 | ICE / Cut | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 33.1 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 34 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern | False | ｶｯﾄ - 押し出し13, a1 |
| 35 | 4.2<1> | LibraryFeature | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し13, ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 |
| 35.1 | ｶｯﾄ - 押し出し13 | ICE / Cut | False | a1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 35.1.1 | a1 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 35.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern | False | ｶｯﾄ - 押し出し13, a1 |
| 36 | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 | FlatPattern | True | 板金, 板金19, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｽｹｯﾁ2 |
| 36.1 | ﾍﾞﾝﾄﾞ-ﾗｲﾝ2 | ProfileFeature | False |  |
| 36.2 | 境界ﾎﾞｯｸｽ2 | ProfileFeature | True |  |
| 36.3 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ1>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 36.4 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ3>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 36.5 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ4>4 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 36.6 | ｽｹｯﾁ変換1 | FoldUpDownSketchFolder | True |  |
| 36.6.1 | ｽｹｯﾁ2-1 | ProfileFeature | False | ｽｹｯﾁ2, ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 |

## 3615-04-bh-T2-fr2.SLDPRT

Configuration: `Default`. Top-level nodes: 32. Evidence JSON: `evidence/api/3615-04-bh-T2-fr2.SLDPRT.json`.

| API path | Name | GetTypeName2 / legacy | Suppressed active | Parents |
| --- | --- | --- | --- | --- |
| 1 | Configuration Comments | ConfigCommentsFolder | False |  |
| 2 | Favorites | FavoriteFolder | False |  |
| 3 | History | HistoryFolder | False |  |
| 4 | Selection Sets | SelectionSetFolder | False |  |
| 5 | Sensors | SensorFolder | False |  |
| 6 | ﾃﾞｻﾞｲﾝ ﾊﾞｲﾝﾀﾞｰ | DocsFolder | False |  |
| 7 | Annotations | DetailCabinet | False |  |
| 7.1 | Notes | NotesAreaFtrFolder | False |  |
| 7.2 | Notes Area | AnnotationViewFeat | False |  |
| 7.3 | Notes1___EndTag___ | FtrFolder | False |  |
| 8 | Markups | InkMarkupFolder | False |  |
| 9 | Lights and Cameras | EnvFolder | False |  |
| 9.1 | Ambient | AmbientLight | False |  |
| 9.2 | Directional1 | DirectionLight | False |  |
| 10 | Solid Bodies | SolidBodyFolder | False |  |
| 10.1 | Sheet<1> | CutListFolder | False |  |
| 11 | Surface Bodies | SurfaceBodyFolder | False |  |
| 12 | Comments | CommentsFolder | False |  |
| 13 | Equations | EqnFolder | False |  |
| 14 | A1100P- | MaterialFolder | False |  |
| 15 | Front | RefPlane | False |  |
| 16 | Top | RefPlane | False |  |
| 17 | Right | RefPlane | False |  |
| 18 | Origin | OriginProfileFeature | False | Front |
| 19 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 20 | 板金19 | SheetMetal | False | 板金 |
| 21 | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 | SMBaseFlange | False | 板金, 板金19, ｽｹｯﾁ1, Right |
| 21.1 | ｽｹｯﾁ1 | ProfileFeature | False | Right, Origin |
| 21.2 | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.3 | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 21.4 | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 22 | Sheet<1> | CutListFolder | False |  |
| 23 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 24 | ｶｯﾄ - 押し出し1 | Cut | False | ｽｹｯﾁ2, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 24.1 | ｽｹｯﾁ2 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ3, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 25 | ｽｹｯﾁ3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 26 | ｶｯﾄ - 押し出し2 | Cut | False | ｽｹｯﾁ3, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 26.1 | ｽｹｯﾁ3 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ4, ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 27 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 28 | ｶｯﾄ - 押し出し11 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 28.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 29 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 30 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 31 | スタッド10x16<1> | LibraryFeature | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｶｯﾄ - 押し出し11, ｶｰﾌﾞ ﾊﾟﾀｰﾝ1, ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 |
| 31.1 | ｶｯﾄ - 押し出し11 | ICE / Cut | False | a, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 31.1.1 | a | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 31.2 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 31.3 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern | False | ｶｯﾄ - 押し出し11, a |
| 32 | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ19 | FlatPattern | True | 板金, 板金19, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 32.1 | ﾍﾞﾝﾄﾞ-ﾗｲﾝ2 | ProfileFeature | False |  |
| 32.2 | 境界ﾎﾞｯｸｽ2 | ProfileFeature | True |  |
| 32.3 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ1>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 32.4 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ3>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 32.5 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ4>9 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 32.6 | ｽｹｯﾁ変換1 | FoldUpDownSketchFolder | True |  |

## 4114-04-HN-C07-12_New.SLDPRT

Configuration: `ﾃﾞﾌｫﾙﾄ`. Top-level nodes: 32. Evidence JSON: `evidence/api/4114-04-HN-C07-12_New.SLDPRT.json`.

| API path | Name | GetTypeName2 / legacy | Suppressed active | Parents |
| --- | --- | --- | --- | --- |
| 1 | Configuration Comments | ConfigCommentsFolder | False |  |
| 2 | お気に入り | FavoriteFolder | False |  |
| 3 | 履歴 | HistoryFolder | False |  |
| 4 | 選択ｾｯﾄ | SelectionSetFolder | False |  |
| 5 | ﾃﾞｻﾞｲﾝ ﾊﾞｲﾝﾀﾞｰ | DocsFolder | False |  |
| 6 | ｱﾉﾃｰﾄ ｱｲﾃﾑ | DetailCabinet | False |  |
| 6.1 | Notes | NotesAreaFtrFolder | False |  |
| 6.2 | 注記領域 | AnnotationViewFeat | False |  |
| 6.3 | Notes1___EndTag___ | NotesAreaFtrFolder | False |  |
| 7 | Markups | InkMarkupFolder | False |  |
| 8 | 照明とｶﾒﾗ | EnvFolder | False |  |
| 8.1 | ｱﾝﾋﾞｴﾝﾄ | AmbientLight | False |  |
| 8.2 | 指向性1 | DirectionLight | False |  |
| 8.3 | 指向性2 | DirectionLight | False |  |
| 8.4 | 指向性3 | DirectionLight | False |  |
| 8.5 | 指向性4 | DirectionLight | False |  |
| 9 | ｾﾝｻｰ | SensorFolder | False |  |
| 10 | ｿﾘｯﾄﾞ ﾎﾞﾃﾞｨ | SolidBodyFolder | False |  |
| 10.1 | ｶｯﾄ-ﾘｽﾄ-ｱｲﾃﾑ1 | CutListFolder | False |  |
| 11 | ｻｰﾌｪｽ ﾎﾞﾃﾞｨ（複数） | SurfaceBodyFolder | False |  |
| 12 | ｺﾒﾝﾄ | CommentsFolder | False |  |
| 13 | 関係式 | EqnFolder | False |  |
| 14 | NSD- | MaterialFolder | False |  |
| 15 | 正面 | RefPlane | False |  |
| 16 | 平面 | RefPlane | False |  |
| 17 | 右側面 | RefPlane | False |  |
| 18 | 原点 | OriginProfileFeature | False | 正面 |
| 19 | X軸 | RefAxis | False | 右側面, 平面 |
| 20 | 左側面 | RefPlane | False | X軸, 右側面 |
| 21 | ｽｹｯﾁ1 | ProfileFeature | False | 正面, 原点 |
| 22 | 板金12 | SheetMetal | False | 板金 |
| 23 | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 | SMBaseFlange | False | 板金, 板金12, ｽｹｯﾁ1, 正面 |
| 23.1 | ｽｹｯﾁ1 | ProfileFeature | False | 正面, 原点 |
| 23.2 | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 23.3 | ﾍﾞｰｽﾍﾞﾝﾄﾞ2 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 23.4 | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 23.5 | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 | SketchBend | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 24 | ｶｯﾄ-ﾘｽﾄ-ｱｲﾃﾑ1 | CutListFolder | False |  |
| 25 | ｽｹｯﾁ6 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 26 | ｶｯﾄ - 押し出し1 | ICE / Cut | False | ｽｹｯﾁ6, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 26.1 | ｽｹｯﾁ6 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 27 | ｴｯｼﾞ-ﾌﾗﾝｼﾞ1 | EdgeFlange | False | 板金, 板金12, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｽｹｯﾁ9 |
| 27.1 | ｽｹｯﾁ9 | ProfileFeature | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 27.2 | ｴｯｼﾞﾍﾞﾝﾄﾞ1 | OneBend | False | ｴｯｼﾞ-ﾌﾗﾝｼﾞ1 |
| 28 | 平面1 | RefPlane | False | ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 29 | ｽｹｯﾁ10 | ProfileFeature | False | 平面1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 30 | とめつぎﾌﾗﾝｼﾞ1 | SMMiteredFlange | False | 板金, 板金12, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ｽｹｯﾁ10, 平面1 |
| 30.1 | ｽｹｯﾁ10 | ProfileFeature | False | 平面1, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1, ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 30.2 | とめつぎﾍﾞﾝﾄﾞ1 | SketchBend | False | とめつぎﾌﾗﾝｼﾞ1 |
| 31 | ADDIN_VCut_Bend_890 | Attribute | False |  |
| 32 | ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ12 | FlatPattern | True | 板金, 板金12, ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1 |
| 32.1 | ﾍﾞﾝﾄﾞ-ﾗｲﾝ2 | ProfileFeature | False |  |
| 32.2 | 境界ﾎﾞｯｸｽ2 | ProfileFeature | True |  |
| 32.3 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ1>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ1 |
| 32.4 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ2>1 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ2 |
| 32.5 | ｽｹｯﾁ変換1 | FoldUpDownSketchFolder | True |  |
| 32.6 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ3>2 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ3 |
| 32.7 | ﾌﾗｯﾄ-<ﾍﾞｰｽﾍﾞﾝﾄﾞ4>2 | UiBend | False | ﾍﾞｰｽﾍﾞﾝﾄﾞ4 |
| 32.8 | ﾌﾗｯﾄ-<ｴｯｼﾞﾍﾞﾝﾄﾞ1>1 | UiBend | False | ｴｯｼﾞﾍﾞﾝﾄﾞ1 |
| 32.9 | ﾌﾗｯﾄ-<とめつぎﾍﾞﾝﾄﾞ1>1 | UiBend | False | とめつぎﾍﾞﾝﾄﾞ1 |
