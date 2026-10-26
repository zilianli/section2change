// 首页和设置页的立交缩略图：由生成层按典型参数画出（tests 里的 ThumbnailTests，设置 THUMBNAIL_DIR 重新生成）
import Cloverleaf from "./thumbs/interchange-Cloverleaf.svg";
import Diamond from "./thumbs/interchange-Diamond.svg";
import DirectionalT from "./thumbs/interchange-DirectionalT.svg";
import ExpresswayNode from "./thumbs/interchange-ExpresswayNode.svg";
import ExpresswayT from "./thumbs/interchange-ExpresswayT.svg";
import PartialCloverleaf from "./thumbs/interchange-PartialCloverleaf.svg";
import Roundabout from "./thumbs/interchange-Roundabout.svg";
import SemiDirectionalT from "./thumbs/interchange-SemiDirectionalT.svg";
import SinglePoint from "./thumbs/interchange-SinglePoint.svg";
import Stack from "./thumbs/interchange-Stack.svg";
import Trumpet from "./thumbs/interchange-Trumpet.svg";
import Turbine from "./thumbs/interchange-Turbine.svg";

export const thumbnails: Record<string, string> = {
  Diamond, ExpresswayNode, SinglePoint, Cloverleaf, PartialCloverleaf, Roundabout, Stack, Turbine,
  ExpresswayT, Trumpet, SemiDirectionalT, DirectionalT,
};
