// 首页和设置页的缩略图：都由 ThumbnailTests 按典型参数生成（井字用替换模式的井字求解器），线宽按取景范围统一。
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
import Wye from "./thumbs/interchange-Wye.svg";
import CompactGrid from "./thumbs/interchange-CompactGrid.svg";

export const thumbnails: Record<string, string> = {
  Diamond, ExpresswayNode, SinglePoint, Cloverleaf, PartialCloverleaf, Roundabout, Stack, Turbine,
  ExpresswayT, Trumpet, SemiDirectionalT, DirectionalT, Wye, CompactGrid,
};
