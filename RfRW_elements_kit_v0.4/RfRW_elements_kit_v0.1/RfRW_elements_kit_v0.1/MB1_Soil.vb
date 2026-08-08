Option Explicit On
Option Strict Off

Imports DBTransactionManager = Autodesk.AutoCAD.DatabaseServices.TransactionManager
Imports System.Math
Imports Shape = Autodesk.Civil.DatabaseServices.Shape
Imports OpenMode = Autodesk.AutoCAD.DatabaseServices.OpenMode
Imports Autodesk.Aec.Geometry
Imports System.Web

Public Class MB1_Soil
    Inherits SATemplate

    ' *************************************************************************
    ' *************************************************************************
    ' *************************************************************************
    '          Name: MB1_Wall
    '
    '   Description: Creates a simple cross-sectional representation of a reinforcement soil wall composed of an array of geogrids.Attachment origin
    '                is at bottom.
    '
    ' Logical Names: Name                       Type       Optional  Description
    '                --------------------------------------------------------------
    '                TargetSurface              Surface    Yes       May be used to judge fill/cut condition
    '
    '
    ' Input Parameters: Name                   Type    Optional    Default Value    Description
    '                -------------------------------------------------------------------------------------------
    '                Side                   long        no          Right            specifies side to place SA on
    '                Width                  double      no          3.0              width of geogrids
    '                Step                   double      no          0.5              step of geogrid layer
    '                GravelSlope            double      no          1.5              0
    '                DrenageOffset          double      no          0.3              0
    '                DrenageElevation       long        no           1               0
    '                GeotextileOverlap      double      no          0.3              0
    '                BaseElevation          double      no          0.15             0
    '                RE520_count            long        no          0                0
    '                RE540_count            long        no          0                0
    '                RE560_count            long        no          0                0
    '                RE570_count            long        no          0                0
    '                RE580_count            long        no          0                0
    '                RE520_length           double      no          3                0
    '                RE540_length           double      no          3                0
    '                RE560_length           double      no          3                0
    '                RE570_length           double      no          3                0
    '                RE580_length           double      no          3                0
    '
    '
    'Output Parameters: Name               Type              Description
    '                ------------------------------------------------------------------
    '                None
    Private Const SideDefault = Utilities.Right  '"right"
    Private Const dGridWidthDefault = 3.0
    Private Const dLayerStepDefault = 0.5
    Private Const dGravelSlopeDefault = 1.5
    Private Const dDrenageOffsetDefault = 0.3
    Private Const dDrenageElevationDefault = 1
    Private Const dGeotextileOverlapDefault = 0.3
    Private Const dBaseElevationDefault = 0.25
    Private Const dRE520_countDefault = 0
    Private Const dRE540_countDefault = 0
    Private Const dRE560_countDefault = 0
    Private Const dRE570_countDefault = 0
    Private Const dRE580_countDefault = 0
    Private Const dSubAsNameDefault = "Участок"
    'Private Const blocksAboveGrid As Integer = 0
    'Private Const dBlocksCount As Integer = 1
    'Private Const dBlockH = 0.5
    Private Const dBlocksInLayout = 5
    Private Const dBlockHeight = 0.5
    Private Const deltaH = 0.000
    Private Const dBlockOffset = 0.0
    Private Const dBlockLength = 1.405
    Private Const WidthDefaultF = 0.8
    Private Const HeightDefaultF = 0.3
    Private Const dPrepHeight = 0.02
    Private Const dPipeSlope = 0.05
    Private Const dPipeStep = 5.0
    Private Const dPipeDiametr = 0.16
    Private Const baseL1 = 0.165
    Private Const baseL2 = 0.135
    Private Const toothH1 = 0.05
    Private Const toothL1 = 0.02
    Private Const cutL1 = 0.03
    Private Const faceHeight = 0.44
    Private Const dDualReinf = False
    Private Const dDualReinfCount = 0
    Private Const dDualReinfAbove = False

    Private Shared _blocksCount As Integer = 0 'необходима для хранения значения на протяжении перестроения всего коридора
    'Private Const dRE520_lengthDefault = 3
    'Private Const dRE540_lengthDefault = 3
    'Private Const dRE560_lengthDefault = 3
    'Private Const dRE570_lengthDefault = 3
    'Private Const dRE580_lengthDefault = 3


    Protected Overrides Sub GetLogicalNamesImplement(corridorState As CorridorState)
        MyBase.GetLogicalNamesImplement(corridorState)

        'retrieve paramater buckets from the corridor state
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong
        'add logical names we used to script
        Dim ParamLong As ParamLong

        ParamLong = paramsLong.Add("Проектный профиль", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Проектный профиль"

        ParamLong = paramsLong.Add("Граница засыпки", ParamLogicalNameType.OffsetTarget)
        ParamLong.DisplayName = "Граница засыпки"

        ParamLong = paramsLong.Add("blocksTop", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Профиль облицовочных блоков"

        ParamLong = paramsLong.Add("BackProf", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Профиль тыльной стороны"
        ParamLong = paramsLong.Add("FrontProf", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Профиль лицевой стороны"
    End Sub
    Protected Overrides Sub GetInputParametersImplement(ByVal corridorState As CorridorState)
        MyBase.GetInputParametersImplement(corridorState)

        ' define collection for long parameters in corridor
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        ' define collection for double parameters in corridor
        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        ' define collection for string parameters in corridor
        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString

        Dim paramsBool As ParamBoolCollection
        paramsBool = corridorState.ParamsBool
        ' Add input parameters we used in this script

        'paramsDouble.Add("Наклон лицевой грани", dFaceAngleDefault)
        'paramsDouble.Add("Горизонтальное смещение", dHorizOffset)
        paramsDouble.Add("Длина георешеток", dGridWidthDefault)
        paramsDouble.Add("Шаг георешеток", dLayerStepDefault)
        paramsDouble.Add("Наклон дренажных призм", dGravelSlopeDefault)
        paramsDouble.Add("Ширина дренажных призм", dDrenageOffsetDefault)
        paramsDouble.Add("Отступ первого слоя георешетки", dBaseElevationDefault)
        paramsDouble.Add("Перехлест геотекстиля", dGeotextileOverlapDefault)
        paramsLong.Add(Utilities.Side, SideDefault)
        paramsLong.Add("Отступ первого слоя дренажа", dDrenageElevationDefault)
        paramsLong.Add("Кол-во RE580", dRE580_countDefault)
        paramsLong.Add("Кол-во RE570", dRE570_countDefault)
        paramsLong.Add("Кол-во RE560", dRE560_countDefault)
        paramsLong.Add("Кол-во RE540", dRE540_countDefault)
        paramsLong.Add("Кол-во RE520", dRE520_countDefault)
        paramsString.Add("Имя участка", dSubAsNameDefault)
        'paramsLong.Add("bCount", dBlocksCount)
        'paramsDouble.Add("bHeight", dBlockH)
        'paramsLong.Add("BlocksAboveGrid", blocksAboveGrid)
        'paramsDouble.Add("RE580_length", dRE580_lengthDefault)
        'paramsDouble.Add("RE570_length", dRE570_lengthDefault)
        'paramsDouble.Add("RE560_length", dRE560_lengthDefault)
        'paramsDouble.Add("RE540_length", dRE540_lengthDefault)
        'paramsDouble.Add("RE520_length", dRE520_lengthDefault)
        'paramsLong.Add("BlocksInLayout", dBlocksInLayout)
        paramsDouble.Add("BlocksDeltaH", deltaH)
        paramsDouble.Add("BlockLength", dBlockLength)
        paramsDouble.Add("Толщина Подготовки", dPrepHeight)
        paramsDouble.Add("Уклон дренажной трубы", dPipeSlope)
        paramsDouble.Add("Шаг дренажных выпусков", dPipeStep)
        paramsDouble.Add("Диаметр дренажной трубы", dPipeDiametr)
        paramsBool.Add("Двойное геоармирование", dDualReinf)
        paramsLong.Add("Кол-во нижних рядов блоков двойного армирования", dDualReinfCount)
        paramsLong.Add("Кол-во верхних рядов блоков двойного армирования", dDualReinfCount)
        paramsBool.Add("Выпуск герешетки из середины блока (выше двойного геоармирования)", dDualReinfAbove)
    End Sub

    Protected Overrides Sub GetOutputParametersImplement(ByVal corridorState As CorridorState)
        MyBase.GetOutputParametersImplement(corridorState)

    End Sub

    Protected Overrides Sub DrawImplement(ByVal corridorState As CorridorState)

        Dim tm As DBTransactionManager
        tm = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.WorkingDatabase.TransactionManager

        Dim oParamsElevationTarget As ParamElevationTargetCollection
        oParamsElevationTarget = corridorState.ParamsElevationTarget

        Dim oParamsOffsetTarget As ParamOffsetTargetCollection
        oParamsOffsetTarget = corridorState.ParamsOffsetTarget
        ' Retrieve parameter buckets from the corridor state
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString

        Dim paramsBool As ParamBoolCollection
        paramsBool = corridorState.ParamsBool
#Region "Присваивание переменным значений параметров"

        Dim side As Long
        Try
            side = paramsLong.Value(Utilities.Side)
        Catch
            side = SideDefault
        End Try
        '----------------------------------------
        'flip about Y axis
        Dim flip As Double
        flip = 1.0#
        If side = Utilities.Left Then
            flip = -1.0#
        End If
        '----------------------------------------
        'geogrid dimensions
        Dim oGridWidth As Double
        Try
            oGridWidth = paramsDouble.Value("Длина георешеток")
        Catch
            oGridWidth = dGridWidthDefault
        End Try
        '----------------------------------------
        Dim gStep As Double
        Try
            gStep = paramsDouble.Value("Шаг георешеток")
        Catch
            gStep = dLayerStepDefault
        End Try
        '----------------------------------------
        Dim oDrenageSlope As Double
        Try
            oDrenageSlope = paramsDouble.Value("Заложение дренажных призм")
        Catch
            oDrenageSlope = dGravelSlopeDefault
        End Try
        '----------------------------------------
        Dim oDrenageOffset As Double
        Try
            oDrenageOffset = paramsDouble.Value("Ширина дренажных призм")
        Catch
            oDrenageOffset = dDrenageOffsetDefault
        End Try
        '----------------------------------------
        Dim oDrenageElevation As Long
        Try
            oDrenageElevation = paramsLong.Value("Отступ первого слоя дренажа")
        Catch
            oDrenageElevation = dDrenageElevationDefault
        End Try
        '----------------------------------------
        Dim oBaseElevation As Double
        Try
            oBaseElevation = paramsDouble.Value("Отступ первого слоя георешетки")
        Catch
            oBaseElevation = dBaseElevationDefault
        End Try
        '----------------------------------------
        Dim geotextileOverlap As Double
        Try
            geotextileOverlap = paramsDouble.Value("Перехлест геотекстиля")
        Catch
            geotextileOverlap = dGeotextileOverlapDefault
        End Try
        '----------------------------------------
        Dim RE580_count As Long
        Try
            RE580_count = paramsLong.Value("Кол-во RE580")
        Catch
            RE580_count = dRE580_countDefault
        End Try
        '----------------------------------------
        Dim RE570_count As Long
        Try
            RE570_count = paramsLong.Value("Кол-во RE570")
        Catch
            RE570_count = dRE570_countDefault
        End Try
        '----------------------------------------
        Dim RE560_count As Long
        Try
            RE560_count = paramsLong.Value("Кол-во RE560")
        Catch
            RE560_count = dRE560_countDefault
        End Try
        '----------------------------------------
        Dim RE540_count As Long
        Try
            RE540_count = paramsLong.Value("Кол-во RE540")
        Catch
            RE540_count = dRE540_countDefault
        End Try
        '----------------------------------------
        Dim RE520_count As Long
        Try
            RE520_count = paramsLong.Value("Кол-во RE520")
        Catch
            RE520_count = dRE520_countDefault
        End Try
        '----------------------------------------
        Dim oSubAsName As String
        Try
            oSubAsName = paramsString.Value("Имя участка")
        Catch
            oSubAsName = dSubAsNameDefault
        End Try
        '----------------------------------------
        Dim dH As Double
        Try
            dH = paramsDouble.Value("BlocksDeltaH")
        Catch
            dH = deltaH
        End Try
        '----------------------------------------
        Dim dL As Double
        Try
            dL = paramsDouble.Value("BlockLength")
        Catch
            dL = dBlockLength
        End Try
        '----------------------------------------
        Dim prepHeight As Double
        Try
            prepHeight = paramsDouble.Value("Толщина Подготовки")
        Catch
            prepHeight = dPrepHeight
        End Try
        '----------------------------------------
        'foundation dimensions
        Dim widthF As Double
        Try
            widthF = paramsDouble.Value("Ширина Фундамента")
        Catch
            widthF = WidthDefaultF
        End Try
        '-----------------------------------------
        Dim heightF As Double
        Try
            heightF = paramsDouble.Value("Высота Фундамента")
        Catch
            heightF = HeightDefaultF
        End Try
        '----------------------------------------
        Dim oPipeStep As Double
        Try
            oPipeStep = paramsDouble.Value("Шаг дренажных выпусков") / 2
        Catch
            oPipeStep = dPipeStep / 2
        End Try
        '-----------------------
        Dim oPipeSlope As Double
        Try
            oPipeSlope = paramsDouble.Value("Уклон дренажной трубы")
        Catch
            oPipeSlope = dPipeSlope
        End Try
        '-----------------------
        Dim oPipeD As Double
        Try
            oPipeD = paramsDouble.Value("Диаметр дренажной трубы")
        Catch
            oPipeD = dPipeDiametr
        End Try
        '-----------------------
        Dim odualR As Boolean
        Dim odualRCountLow As Long
        Dim odualRCountUp As Long
        Dim odualRtoMiddle As Boolean
        Try
            odualR = paramsBool.Value("Двойное армирование")
            If odualR Then
                Try
                    odualRCountLow = paramsLong.Value("Кол-во нижних рядов блоков двойного армирования")
                Catch
                    odualRCountLow = dDualReinfCount
                End Try
            End If
            If odualR Then
                Try
                    odualRCountUp = paramsLong.Value("Кол-во верхних рядов блоков двойного армирования")
                Catch
                    odualRCountUp = dDualReinfCount
                End Try
            End If
            If odualR Then
                Try
                    odualRtoMiddle = paramsBool.Value("Выпуск герешетки из середины блока (выше двойного геоармирования)")
                Catch
                    odualRtoMiddle = dDualReinfAbove
                End Try
            End If
        Catch
            odualR = dDualReinf
        End Try
        '-----------------------------------------------
#End Region
        ' Check user input
        If oGridWidth < 3 Then
            Utilities.RecordError(corridorState, CorridorError.ValueTooSmall, "Длина георешеток", "Geogrid")
            oGridWidth = dGridWidthDefault
        End If

        If gStep <= 0 Then
            Utilities.RecordError(corridorState, CorridorError.ValueShouldNotBeLessThanOrEqualToZero, "Шаг георешеток", "Geogrid")
            gStep = dLayerStepDefault
        End If

        Dim oOrigin As New PointInMem
        Dim oCurrentAlignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, oCurrentAlignmentId, oOrigin)

        Dim dWallWidthOffset As Double = (oGridWidth + 1.0) * flip 'soil width
        Dim blockStep = dBlockHeight + dH

        If corridorState.Mode <> CorridorMode.Layout Then 'для сечений коридора
            If corridorState.Mode = CorridorMode.None Then
                Throw New Exception("NONE")
            End If
            '--------------------------------------------------------
            'анализируем наличие целей для сбора информации в сечении
            '--------------------------------------------------------

            Dim elevationTarget As SlopeElevationTarget
            Try
                elevationTarget = oParamsElevationTarget.Value("Проектный профиль")
            Catch
                elevationTarget = Nothing
            End Try

            Dim hasWallHeightProfile As Boolean
            hasWallHeightProfile = False
            Dim dWallHeightElevation As Double

            If Not elevationTarget Is Nothing Then
                'get elevation on elevationTarget
                Try
                    dWallHeightElevation = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation, side) - (oOrigin.Elevation)
                    hasWallHeightProfile = True
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Проектный профиль", "RetainWallVertical")
                End Try
            End If
            'определяем профиль облицовочных блоков (если имеется)
            Dim blocksElevTarget As SlopeElevationTarget
            Try
                blocksElevTarget = oParamsElevationTarget.Value("blocksTop")
            Catch
                blocksElevTarget = Nothing
            End Try

            Dim hasWallBlocksProfile As Boolean
            hasWallBlocksProfile = False
            Dim blocksHeight As Double

            If Not blocksElevTarget Is Nothing Then
                'получим высоту по профилю
                Try
                    blocksHeight = blocksElevTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation) - oOrigin.Elevation
                    hasWallBlocksProfile = True

                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "blocksTop", "RetainWallVertical")
                End Try
                'сечение по заданному профилю высоты блоков+проектному

            End If
            'Определяем глубину отступа для грунта засыпки
            Dim offsetTarget As WidthOffsetTarget
            Try
                offsetTarget = oParamsOffsetTarget.Value("Граница засыпки")
            Catch
                offsetTarget = Nothing
            End Try

            Dim hasWallOffsetTarget As Boolean
            hasWallOffsetTarget = False

            Dim xOffset As Double
            Dim yOffset As Double
            Dim soilOffset As Double

            If Not offsetTarget Is Nothing Then
                Try
                    Utilities.CalcAlignmentOffsetToThisAlignment(oCurrentAlignmentId, corridorState.CurrentStation, offsetTarget, soilOffset, xOffset, yOffset)
                    hasWallOffsetTarget = True
                    dWallWidthOffset = soilOffset - oOrigin.Offset
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Граница засыпки", "RetainWallHorizontal")
                End Try
            End If

            Dim rows As Integer
            If hasWallBlocksProfile Then 'если есть верхний профиль облицовочных блоков
                rows = CType(blocksHeight / blockStep, Integer)
                createAddStationsForProfile(tm, corridorState, blocksElevTarget)

            Else 'в случае отсутствия профиля для определения высоты облицовки (для первого прохода например)
                'в начале каждого региона(области) добавляем сечения в пикетах шага облицовочного блока 
                If corridorState.CurrentStation = corridorState.CurrentRegionStartStation Then
                    'создаем доп.сечения
                    createAddStations(tm, corridorState, blockStep, dL, elevationTarget) 'доп сечения для облицовки
                    createPipeAddStations(tm, corridorState, oPipeStep, oPipeSlope) 'доп сечения для трубы
                    ' Рассчитываем новое количество блоков на основе высоты
                    Dim divisor = blockStep * 1000
                    _blocksCount = dWallHeightElevation * 1000 \ divisor
                    'доп условие: если стена опускается с самого начала
                    Dim firstTop As Double = 0
                    While firstTop <= dL / 2
                        If isStep(tm, corridorState, firstTop) Then
                            _blocksCount -= 1
                        End If
                        firstTop += 0.001
                    End While
                    'доп.условие2: если на расстоянии до первого скачка блоков проектный профиль ниже облицовочного блока
                    Dim firstStepStation As Double
                    firstStepAtCurrRegion(tm, corridorState, firstStepStation)
                    Dim elevAtFirstStep = elevationTarget.GetElevation(oCurrentAlignmentId, firstStepStation)
                    If (elevAtFirstStep - oOrigin.Elevation) < (_blocksCount * blockStep) Then
                        _blocksCount -= 1
                    End If
                End If

                'условие для переопределения высоты облицовки
                If isStep(tm, corridorState, corridorState.CurrentStation) And corridorState.CurrentStation <> corridorState.CurrentRegionStartStation And corridorState.CurrentStation <> corridorState.CurrentRegionStartStation + 0.001 Then
                    'вспомогательные вектора до и после скачка для оценки направления проектного профиля
                    Dim beforeStep = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation - 0.01)
                    Dim afterStep = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation + 0.01)
                    'сравниваем текущую высоту по блокам и высоту луча(общую высоту стенки)
                    If beforeStep < afterStep Then
                        Dim dif As Integer = (afterStep - oOrigin.Elevation - _blocksCount * dBlockHeight) * 1000 \ (dBlockHeight * 1000)
                        _blocksCount += dif
                    ElseIf beforeStep > afterStep Then
                        Dim dif As Integer = Math.Abs((_blocksCount * dBlockHeight - (afterStep - oOrigin.Elevation)) * 1000 \ (dBlockHeight * 1000)) + 1
                        _blocksCount -= dif
                    Else
                        Throw New Exception("что-то неладное")
                    End If

                End If
                rows = _blocksCount
            End If
            'Определяем есть ли профиль ТЫЛЬНОЙ стороны и его значение
            Dim backTarget As SlopeElevationTarget
            Try
                backTarget = oParamsElevationTarget.Value("BackProf")
            Catch
                backTarget = Nothing
            End Try

            Dim hasWallBackProfile As Boolean = False
            Dim dWallBackElevation As Double

            If Not backTarget Is Nothing Then
                'get elevation on elevationTarget
                Try
                    dWallBackElevation = backTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation, side) - (oOrigin.Elevation)
                    hasWallBackProfile = True
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "BackProf", "RetainWallVertical")
                End Try
            End If
            'Определяем есть ли профиль ЛИЦЕВОЙ стороны и его значение
            Dim frontTarget As SlopeElevationTarget
            Try
                frontTarget = oParamsElevationTarget.Value("FrontProf")
            Catch
                frontTarget = Nothing
            End Try

            Dim hasWallFrontProfile As Boolean = False
            Dim dWallFrontElevation As Double

            If Not frontTarget Is Nothing Then
                'get elevation on elevationTarget
                Try
                    dWallFrontElevation = frontTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation, side) - (oOrigin.Elevation)
                    hasWallFrontProfile = True
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "FrontProf", "RetainWallVertical")
                End Try
            End If
            'paramsLong.Item("BlocksCount").Value = rows
            Dim lowerLayerH As Double = oBaseElevation
            Dim levelWidth = 0.35 'ширина выравнивающего слоя по облицовке
            '--------------------------------
            'построение конструкции в сечении
            '--------------------------------
            Dim pointToSoil As New PointInMem
            Dim pointToSubbase As New PointInMem 'точка для вставки щебеночной подготовки
            Dim zeroPoint As New PointInMem With {
                .Offset = 0,
                .Elevation = 0
                }
            'создание облицовочных блоков
            createCladdingMB(corridorState, dWallHeightElevation, flip, rows, blockStep, dH, levelWidth, zeroPoint, pointToSoil)

            pointToSoil.Offset += flip * (baseL1 + toothL1) 'т.к. точка вставки = оси раскладки блока, смещаем точку на величину (горизонтальную) зуба+задней стороны
            pointToSoil.Elevation -= toothH1
            'создание армогрунта
            If frontTarget Is Nothing Then
                dWallFrontElevation = dWallHeightElevation
            End If
            wallCreate(corridorState, dWallFrontElevation, dWallWidthOffset, dWallBackElevation, oGridWidth, gStep,
                       lowerLayerH, oDrenageElevation, oDrenageOffset, oDrenageSlope, geotextileOverlap,
                       flip, RE520_count, RE540_count, RE560_count, RE570_count, RE580_count,
                       rows, blockStep, hasWallOffsetTarget, hasWallBackProfile, oPipeStep, oPipeSlope, oPipeD, pointToSoil, odualR, odualRCountLow, odualRCountUp, odualRtoMiddle)

            'SubBase(corridorState, flip, widthF, heightF, dWallWidthOffset, geotextileOverlap, oBaseElevation, dBlockWidth, prepHeight, dFaceAngleDefault, pointToSubbase, hasWallOffsetTarget, oSubAsName)

        Else 'для представления шаблона конструкции
            '----------------------------------
            'строим шаблон конструкции
            '----------------------------------
            Dim levelWidth = 0.35
            Dim levelH = 0.1
            'Dim dWallHeight = blockLayers * (dBlockHeight + dH) + levelH
            Dim dWallHeightElevation As Double = oBaseElevation + (RE520_count + RE540_count + RE560_count + RE570_count + RE580_count) * gStep + levelH - gStep * (odualRCountLow + odualRCountUp)
            Dim blockLayers As Integer = dWallHeightElevation * 1000 \ blockStep * 1000
            Dim hasWallOffsetTarget As Boolean
            hasWallOffsetTarget = False
            Dim hasWallBackProfile As Boolean = False
            Dim dWallBackElevation As Double
            Dim lowerLayerH As Double = oBaseElevation

            Dim pointToSoil As New PointInMem  'точка для вставки армогрунта
            Dim pointToSubbase As New PointInMem 'точка для вставки щебеночной подготовки
            'создание облицовочных блоков
            createCladdingMB(corridorState, dWallHeightElevation, flip, blockLayers, blockStep, dH, levelWidth, oOrigin, pointToSoil)

            pointToSoil.Offset += flip * (baseL1 + toothL1) 'т.к. точка вставки = оси раскладки блока, смещаем точку на величину (горизонтальную) зуба+задней стороны
            pointToSoil.Elevation -= toothH1

            wallCreate(corridorState, dWallHeightElevation, dWallWidthOffset, dWallBackElevation, oGridWidth, gStep,
                       lowerLayerH, oDrenageElevation, oDrenageOffset, oDrenageSlope, geotextileOverlap,
                       flip, RE520_count, RE540_count, RE560_count, RE570_count, RE580_count,
                       blockLayers, blockStep, hasWallOffsetTarget, hasWallBackProfile, oPipeStep, oPipeSlope, oPipeD, pointToSoil, odualR, odualRCountLow, odualRCountUp, odualRtoMiddle)


            'SubBase(corridorState, flip, widthF, heightF, dWallWidthOffset, geotextileOverlap, oBaseElevation, dBlockWidth, prepHeight, dFaceAngleDefault, pointToSubbase, hasWallOffsetTarget, oSubAsName)

        End If
        ' Обновляем входные параметры (если требуется)
        Dim param As IParam
        param = paramsDouble.Add("Длина георешеток", oGridWidth)
        param = paramsDouble.Add("Шаг георешеток", gStep)
        param = paramsDouble.Add("Заложение дренажных призм", oDrenageSlope)
        param = paramsDouble.Add("Ширина дренажных призм", oDrenageOffset)
        param = paramsDouble.Add("Отступ первого слоя георешетки", oBaseElevation)
        param = paramsDouble.Add("Перехлест геотекстиля", geotextileOverlap)
        param = paramsLong.Add(Utilities.Side, side)
        param = paramsLong.Add("Отступ первого слоя дренажа", oDrenageElevation)
        param = paramsLong.Add("Кол-во RE580", RE580_count)
        param = paramsLong.Add("Кол-во RE570", RE570_count)
        param = paramsLong.Add("Кол-во RE560", RE560_count)
        param = paramsLong.Add("Кол-во RE540", RE540_count)
        param = paramsLong.Add("Кол-во RE520", RE520_count)
        param = paramsString.Add("Имя участка", oSubAsName)
        param = paramsDouble.Add("BlocksDeltaH", dH)
        param = paramsDouble.Add("BlockLength", dL)
        param = paramsDouble.Add("Толщина Подготовки", prepHeight)
        param = paramsDouble.Add("Уклон дренажной трубы", oPipeSlope)
        param = paramsDouble.Add("Шаг дренажных выпусков", oPipeStep)
        param = paramsDouble.Add("Диаметр дренажной трубы", oPipeD)
        param = paramsBool.Add("Двойное армирование", odualR)
        param = paramsLong.Add("Кол-во нижних рядов блоков двойного армирования", odualRCountLow)
        param = paramsLong.Add("Кол-во верхних рядов блоков двойного армирования", odualRCountUp)
        param = paramsBool.Add("Выпуск герешетки из середины блока (выше двойного геоармирования)", odualRtoMiddle)
    End Sub

#Region "Создание армогрунта"
    Private Sub wallCreate(ByVal corridorState As CorridorState,
                           ByVal wallHeight As Double,
                           ByVal wallWidth As Double,
                           ByVal wallBackHeight As Double,
                           ByVal gridWidth As Double,
                           ByVal verticalStep As Double,
                           ByVal baseLayerStep As Double,
                           ByVal drenageElevLayer As Long,
                           ByVal drenageWidth As Double,
                           ByVal drenageSlope As Double,
                           ByVal geotxtOverlap As Double,
                           ByVal flipValue As Double,
                           ByVal RE520Count As Long,
                           ByVal RE540Count As Long,
                           ByVal RE560Count As Long,
                           ByVal RE570Count As Long,
                           ByVal RE580Count As Long,
                           ByVal blocksCount As Integer,
                           ByVal blockHeight As Double,
                           ByVal hasTargetOffset As Boolean,
                           ByVal hasTargetElevation As Boolean,
                           ByVal pipeStep As Double,
                           ByVal pipeSlope As Double,
                           ByVal pipeDiameter As Double,
                           ByVal startInputPoint As PointInMem,
                           ByVal IsDualReinf As Boolean,
                           ByVal dualReinfCountLow As Long,
                           ByVal dualReinfCountUp As Long,
                           ByVal IsAboveDualRtoMid As Boolean
                           )
        'далее в качестве точек вставки используем "точки из памяти"
        Dim insertPoint As New PointInMem
        Dim elevatP As Double = startInputPoint.Elevation 'переменные для записи значений отметки и отступа
        Dim offsetP As Double = startInputPoint.Offset
        insertPoint.Offset = offsetP 'присваиваем значения опорной точке
        insertPoint.Elevation = elevatP

        'определим кол-во слоев
        Dim layers As Integer
        layers = RE580Count + RE570Count + RE560Count + RE540Count + RE520Count
        Dim allLayers = layers
        If layers > blocksCount And blocksCount < (allLayers - dualReinfCountUp * 2) - dualReinfCountLow Then 'условие кол-ва слоев георешеток НЕ должно быть выше облицовки
            layers = blocksCount + dualReinfCountLow
        ElseIf layers > blocksCount And blocksCount >= (allLayers - dualReinfCountUp * 2) - dualReinfCountLow Then
            layers = blocksCount + dualReinfCountLow + dualReinfCountUp
        End If

        Dim geotextileName As String = "Геотекстиль"
        Dim soilName As String = "Дренирующий грунт"
        Dim drenageName As String = "Щебень дренажной призмы"
        Dim gridNameRE520 As String = "Георешетка RE520"
        Dim gridNameRE540 As String = "Георешетка RE540"
        Dim gridNameRE560 As String = "Георешетка RE560"
        Dim gridNameRE570 As String = "Георешетка RE570"
        Dim gridNameRE580 As String = "Георешетка RE580"

        Dim isLast As Boolean = False
        Dim isDrenLayers As Boolean = False
        Dim isFirst As Boolean = True
        Dim i As Integer = 0

        'песок ниже первой георешетки
        createLowerLayer(corridorState, soilName, wallWidth, baseLayerStep, flipValue, insertPoint, hasTargetOffset)
        elevatP += baseLayerStep
        'offsetP += baseLayerStep * Math.Tan(faceAngle * Math.PI / 180) * flipValue
        i += 1
        'ЦИКЛ СОЗДАЮЩИЙ СЛОИ АРМОГРУНТА И ГЕОРЕШЕТКИ (без верхнего слоя)
        Dim reinfStep As Double
        Do While layers > i
            If IsDualReinf And i < dualReinfCountLow * 2 Then 'условия для слоев имеющих двойное армирование и меньших указаного значения двойного армирования(нижнего)
                reinfStep = verticalStep / 2
            ElseIf IsDualReinf And i > allLayers - dualReinfCountUp * 2 Then
                reinfStep = verticalStep / 2
            Else 'если нет двойного армирования или выше значения двойного армирования
                reinfStep = verticalStep 'высота вертикального шага слоя георешеток
            End If
            If IsDualReinf And i = dualReinfCountLow * 2 And IsAboveDualRtoMid Then 'условие для смещения (чтобы выпуск был из середины блока) для слоев выше двойного армирования(нижнего)
                reinfStep = verticalStep - baseLayerStep
            ElseIf IsDualReinf And i = allLayers - dualReinfCountUp * 2 And dualReinfCountUp > 0 Then
                reinfStep = verticalStep - baseLayerStep
            End If

            'testPoint = testPoints.Add(offsetP, elevatP, i.ToString())
            insertPoint.Offset = offsetP
            insertPoint.Elevation = elevatP
            If i <= drenageElevLayer Then 'слои ниже дренажной призмы
                If i = drenageElevLayer Then
                    isLast = True
                    If layers > i Then
                        isDrenLayers = True
                    End If
                End If
                createNonDrenageLayer(corridorState, soilName, wallWidth, reinfStep, drenageWidth, flipValue, insertPoint, geotxtOverlap, geotextileName, hasTargetOffset, isLast, isDrenLayers)
            Else 'If drenageElevLayer < i And i < layers Then 'слои с дренажной призмой
                createDrenageLayer(corridorState, drenageName, soilName, geotextileName, wallWidth, reinfStep, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, hasTargetOffset, isFirst, pipeStep, pipeSlope, pipeDiameter)
                isFirst = False
            End If
            gridlayers(corridorState, i, insertPoint, flipValue, gridWidth, gridNameRE580, gridNameRE570, gridNameRE560, gridNameRE540, gridNameRE520, RE580Count, RE570Count, RE560Count, RE540Count, RE520Count) 'добавляем слой георешетки
            elevatP += reinfStep
            'offsetP += dX
            i += 1
        Loop
        reinfStep = verticalStep
        insertPoint.Offset = offsetP
        insertPoint.Elevation = elevatP
        'добавляем еще один слой решетки
        gridlayers(corridorState, i, insertPoint, flipValue, gridWidth, gridNameRE580, gridNameRE570, gridNameRE560, gridNameRE540, gridNameRE520, RE580Count, RE570Count, RE560Count, RE540Count, RE520Count) 'добавляем слой георешетки
        'проводим анализ оставшегося пространства
        Dim reminder As Double = wallHeight - elevatP 'остаток
        If i <= drenageElevLayer Then
            If reminder > reinfStep Then
                createNonDrenageLayer(corridorState, soilName, wallWidth, reinfStep, drenageWidth, flipValue, insertPoint, geotxtOverlap, geotextileName, hasTargetOffset, isLast, isDrenLayers)
                reminder -= reinfStep
                elevatP += reinfStep
                insertPoint.Offset = offsetP
                insertPoint.Elevation = elevatP
            End If
            createLastNonDrenageLayer(corridorState, soilName, wallWidth, reminder, wallBackHeight, flipValue, insertPoint, geotxtOverlap, geotextileName, hasTargetOffset, hasTargetElevation)
        Else
            If reminder > reinfStep Then
                createDrenageLayer(corridorState, drenageName, soilName, geotextileName, wallWidth, reinfStep, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, hasTargetOffset, isFirst, pipeStep, pipeSlope, pipeDiameter)
                reminder -= reinfStep
                elevatP += reinfStep
                insertPoint.Offset = offsetP
                insertPoint.Elevation = elevatP
            End If
            createLastDrenageLayer(corridorState, drenageName, soilName, geotextileName, wallWidth, reinfStep, wallBackHeight, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, hasTargetOffset, hasTargetElevation, isFirst, reminder)
        End If
        ' If blocksReminder < 3 And reminder <= verticalStep Then
        '     createLastDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, isFirst, reminder)
        ' ElseIf blocksReminder < 3 And reminder > verticalStep Then
        '     createDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, isFirst, pipeStep, pipeSlope, pipeDiameter)
        '     reminder -= verticalStep
        '     elevatP += verticalStep
        '     offsetP += dX
        '     insertPoint.Offset = offsetP
        '     insertPoint.Elevation = elevatP
        '     createLastDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, isFirst, reminder)
        ' ElseIf blocksReminder >= 3 Then
        '     createLastDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, isFirst, blockHeight)
        '     reminder -= blockHeight
        '     elevatP += blockHeight
        '     offsetP += blockHeight * Math.Tan(faceAngle * Math.PI / 180) * flipValue
        '     insertPoint.Offset = offsetP
        '     insertPoint.Elevation = elevatP
        '     i += 1
        '     'добавляем еще один слой решетки
        '     gridlayers(corridorState, i, insertPoint, flipValue, gridWidth, gridNameRE580, gridNameRE570, gridNameRE560, gridNameRE540, gridNameRE520, RE580Count, RE570Count, RE560Count, RE540Count, RE520Count) 'добавляем слой георешетки
        ' Else
        '     Throw New Exception("какая-то лажа с верхними слоями армогрунта")
        'End If

    End Sub
    'создание георешетки
    Private Sub createGeogrid(ByVal corridorState As CorridorState,
                              ByVal linkName As String,
                              ByVal geogridWidth As Double,
                              ByVal flipValue As Double,
                              ByVal pointToInsert As PointInMem)
        '---------------------------------------------------------
        ' создание точек и связи между ними
        '---------------------------------------------------------
        Dim geogridPoints As PointCollection
        geogridPoints = corridorState.Points

        Dim geogridLinks As LinkCollection
        geogridLinks = corridorState.Links

        Dim gridPoint1 As Point
        Dim gridPoint2 As Point
        Dim gridLink As Link

        Dim gridF As String = linkName + "_лицевая"

        gridPoint1 = geogridPoints.Add(pointToInsert.Offset, pointToInsert.Elevation, gridF)
        gridPoint2 = geogridPoints.Add(pointToInsert.Offset + geogridWidth * flipValue, pointToInsert.Elevation, linkName + "_тыльная")
        gridLink = geogridLinks.Add(gridPoint1, gridPoint2, linkName)

    End Sub
    'добавление соответствующего слоя георешетки
    Private Sub gridlayers(ByVal corridorstate As CorridorState,
                           ByVal i As Integer,
                           ByVal insertPoint As PointInMem,
                           ByVal flipValue As Double,
                           ByVal gridWidth As Double,
                           ByVal gridNameRE580 As String,
                           ByVal gridNameRE570 As String,
                           ByVal gridNameRE560 As String,
                           ByVal gridNameRE540 As String,
                           ByVal gridNameRE520 As String,
                           ByVal RE580Count As Long,
                           ByVal RE570Count As Long,
                           ByVal RE560Count As Long,
                           ByVal RE540Count As Long,
                           ByVal RE520Count As Long
                           )
        Try 'логика присваивания названия слоя
            If i <= RE580Count Then
                'linkName = subAsName + " " + i.ToString + "_RE580"
                createGeogrid(corridorstate, gridNameRE580, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count < i And i <= RE580Count + RE570Count Then
                'linkName = subAsName + " " + i.ToString + "_RE570"
                createGeogrid(corridorstate, gridNameRE570, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count + RE570Count < i And i <= RE580Count + RE570Count + RE560Count Then
                'linkName = subAsName + " " + i.ToString + "_RE560"
                createGeogrid(corridorstate, gridNameRE560, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count + RE570Count + RE560Count < i And i <= RE580Count + RE570Count + RE560Count + RE540Count Then
                'linkName = subAsName + " " + i.ToString + "_RE540"
                createGeogrid(corridorstate, gridNameRE540, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count + RE570Count + RE560Count + RE540Count < i And i <= RE580Count + RE570Count + RE560Count + RE540Count + RE520Count Then
                'linkName = subAsName + " " + i.ToString + "_RE520"
                createGeogrid(corridorstate, gridNameRE520, gridWidth, flipValue, insertPoint)
            End If
        Catch
            Utilities.RecordWarning(corridorstate, CorridorError.None, "no reinforcement", "ReinfSoilArray")
        End Try
    End Sub
    'создание самого нижнего слоя
    Private Sub createLowerLayer(ByVal corridorState As CorridorState,
                                 ByVal soilName As String,
                                 ByVal soilWidth As Double,
                                 ByVal layerHeight As Double,
                                 ByVal flipValue As Double,
                                 ByVal pointToInsert As PointInMem,
                                 ByVal hasTargetOffset As Boolean
                                 )
        '----------------------------
        'sand before first grid
        '----------------------------
        'Dim faceSlope As Double = faceAngle * (Math.PI / 180)
        'Dim fSOffset As Double = layerHeight * Tan(faceSlope) * flipValue 'firstStepOffset
        'создание коллекций для точек и связей
        Dim sandPoints As PointCollection = corridorState.Points
        Dim sandLinks As LinkCollection = corridorState.Links
        Dim sandShapes As ShapeCollection = corridorState.Shapes

        Dim sandPoint1 As Point
        Dim sandPoint2 As Point
        Dim sandPoint3 As Point
        Dim sandPoint4 As Point

        Dim sandLink1 As Link
        Dim sandLink2 As Link
        Dim sandLink3 As Link
        Dim sandLink4 As Link

        Dim sandShape1 As Shape
        'имена точек при построении сечения
        Dim sandPointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 1
        Dim sandPointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 2
        Dim sandPointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 3
        Dim sandPointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 4
        'имена связей при построении сечения
        Dim sandLinkName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandUpBase"
        Dim sandLinkName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandDownBase"
        'создание точек контура песка
        sandPoint1 = sandPoints.Add(pointToInsert.Offset, pointToInsert.Elevation, sandPointName1)
        sandPoint2 = sandPoints.Add(sandPoint1.Offset, sandPoint1.Elevation + layerHeight, sandPointName2)
        'проверка наличия цели для отступа(ширины стены)
        If hasTargetOffset Then
            sandPoint3 = sandPoints.Add(soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(soilWidth, sandPoint1.Elevation, sandPointName4)
        Else
            sandPoint3 = sandPoints.Add(sandPoint2.Offset + soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(sandPoint1.Offset + soilWidth, sandPoint1.Elevation, sandPointName4)
        End If
        'создание линий контура песка
        sandLink1 = sandLinks.Add(sandPoint1, sandPoint2, soilName)
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, "Низ дренирующего грунта")
        sandLink3 = sandLinks.Add(sandPoint3, sandPoint4, soilName)
        sandLink4 = sandLinks.Add(sandPoint4, sandPoint1, soilName)
        ' создание заполнения контура песка
        sandShape1 = sandShapes.Add(sandLink1, sandLink2, sandLink3, sandLink4, soilName)
    End Sub
    'создание слоя засыпки дренирующим грунтом
    Private Sub createNonDrenageLayer(ByVal corridorState As CorridorState,
                                      ByVal soilName As String,
                                      ByVal soilWidth As Double,
                                      ByVal layerHeight As Double,
                                      ByVal drenageWidth As Double,
                                      ByVal flipValue As Double,
                                      ByVal pointToInsert As PointInMem,
                                      ByVal geotxtOverlap As Double,
                                      ByVal geotextileName As String,
                                      ByVal hasTargetOffset As Boolean,
                                      ByVal isLastlayer As Boolean,
                                      ByVal isAnyDrenageLayers As Boolean
                                      )
        '----------------------------
        'sand layer
        '----------------------------
        'создание коллекций для точек,связей и форм
        Dim sandPoints As PointCollection = corridorState.Points
        Dim sandLinks As LinkCollection = corridorState.Links
        Dim sandShapes As ShapeCollection = corridorState.Shapes

        Dim sandPoint1 As Point
        Dim sandPoint2 As Point
        Dim sandPoint3 As Point
        Dim sandPoint4 As Point

        Dim sandLink1 As Link
        Dim sandLink2 As Link
        Dim sandLink3 As Link
        Dim sandLink4 As Link

        Dim sandShape1 As Shape
        'имена точек при построении сечения
        Dim sandPointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 1
        Dim sandPointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 2
        Dim sandPointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 3
        Dim sandPointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 4
        'имена связей при построении сечения
        'Dim sandLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandUpBase"
        'Dim sandLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandDownBase"
        'создание точек контура песка
        sandPoint1 = sandPoints.Add(pointToInsert.Offset, pointToInsert.Elevation, sandPointName1)
        sandPoint2 = sandPoints.Add(sandPoint1.Offset, sandPoint1.Elevation + layerHeight, sandPointName2)
        'проверка наличия цели для отступа(ширины стены)
        If hasTargetOffset Then
            sandPoint3 = sandPoints.Add(soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(soilWidth, sandPoint1.Elevation, sandPointName4)
        Else
            sandPoint3 = sandPoints.Add(sandPoint2.Offset + soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(sandPoint1.Offset + soilWidth, sandPoint1.Elevation, sandPointName4)
        End If
        'создание линий контура песка
        sandLink1 = sandLinks.Add(sandPoint1, sandPoint2, soilName)
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, "")
        sandLink3 = sandLinks.Add(sandPoint3, sandPoint4, soilName)
        sandLink4 = sandLinks.Add(sandPoint4, sandPoint1, "")
        ' создание заполнения контура песка
        sandShape1 = sandShapes.Add(sandLink1, sandLink2, sandLink3, sandLink4, soilName)
        '-------------------------
        'geotextile
        '-------------------------
        'вычисляем вспомогательные параметры
        Dim gtxtOffset = geotxtOverlap * flipValue
        'создание коллекций для точек и связей
        Dim geotxtPoints As PointCollection = corridorState.Points
        Dim geotxtLinks As LinkCollection = corridorState.Links
        'объявим точки для геотекстиля
        Dim geotextilePoint1 As Point
        Dim geotextilePoint2 As Point
        Dim geotextilePoint3 As Point
        Dim geotextilePoint4 As Point
        'объявим связи для геотекстиля
        Dim geotextileLink1 As Link
        Dim geotextileLink2 As Link
        Dim geotextileLink3 As Link

        Dim geotextilePointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 1
        Dim geotextilePointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 2
        Dim geotextilePointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 3
        Dim geotextilePointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 4

        geotextilePoint1 = geotxtPoints.Add(sandPoint1.Offset + gtxtOffset, sandPoint1.Elevation, geotextilePointName1)
        geotextilePoint2 = geotxtPoints.Add(sandPoint1.Offset, sandPoint1.Elevation, geotextilePointName2)
        geotextilePoint3 = geotxtPoints.Add(sandPoint2.Offset + 0.001 * flipValue, sandPoint2.Elevation, geotextilePointName3)
        'проверка наличия сверху слоя с дренажом
        If isLastlayer And isAnyDrenageLayers Then
            geotextilePoint4 = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset + drenageWidth * flipValue, sandPoint2.Elevation, geotextilePointName4)
        Else
            geotextilePoint4 = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset, sandPoint2.Elevation, geotextilePointName4)
        End If
        'Dim geotextileLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileUp"
        'Dim geotextileLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileDown"

        geotextileLink1 = geotxtLinks.Add(geotextilePoint1, geotextilePoint2, geotextileName)
        geotextileLink2 = geotxtLinks.Add(geotextilePoint2, geotextilePoint3, geotextileName)
        geotextileLink3 = geotxtLinks.Add(geotextilePoint3, geotextilePoint4, geotextileName)
    End Sub
    'создание слоя засыпки с пристеночным дренажом
    Private Sub createDrenageLayer(ByVal corridorState As CorridorState,
                                   ByVal drenageName As String,
                                   ByVal soilName As String,
                                   ByVal gtxtName As String,
                                   ByVal soilWidth As Double,
                                   ByVal layerHeight As Double,
                                   ByVal drenageWidth As Double,
                                   ByVal drenageSlope As Double,
                                   ByVal flipValue As Double,
                                   ByVal pointToInsert As PointInMem,
                                   ByVal geotxtOverlap As Double,
                                   ByVal hasTargetOffset As Boolean,
                                   ByVal isFirstlayer As Boolean,
                                   ByVal pipeStep As Double,
                                   ByVal pipeSlope As Double,
                                   ByVal pipeDiameter As Double
                                   )
        'вычисляем вспомогательные параметры
        'Dim faceSlope As Double = faceAngle * (Math.PI / 180)
        'Dim fOffset As Double = layerHeight * Math.Tan(faceSlope) * flipValue 'layer FaceOffset
        Dim dOffset As Double = layerHeight * drenageSlope * flipValue 'layer DrenageOffset
        Dim gOffset As Double = drenageWidth * flipValue 'gravel offset
        Dim gtxtOffset = geotxtOverlap * flipValue 'перехлест геотекстиля
        '----------------------------
        'drenage layer
        '----------------------------
        'объявляем коллекции элементов
        Dim drenagePoints As PointCollection = corridorState.Points
        Dim drenageLinks As LinkCollection = corridorState.Links
        Dim drenageShapes As ShapeCollection = corridorState.Shapes
        'имена точек щебня
        Dim drPointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 1
        Dim drPointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 2
        Dim drPointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 3
        Dim drPointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 4
        'строим слой по точкам
        Dim drPoint1 = drenagePoints.Add(pointToInsert.Offset, pointToInsert.Elevation, drPointName1)
        Dim drPoint2 = drenagePoints.Add(drPoint1.Offset, drPoint1.Elevation + layerHeight, drPointName2)
        Dim drPoint3 = drenagePoints.Add(drPoint1.Offset + gOffset + dOffset, drPoint2.Elevation, drPointName3)
        Dim drPoint4 = drenagePoints.Add(pointToInsert.Offset + gOffset, pointToInsert.Elevation, drPointName4)
        'declare description for links
        ' Dim drLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "gravelUp"
        ' Dim drLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "Drenagelayer"
        'create links of gravel layer
        Dim drLink1 = drenageLinks.Add(drPoint1, drPoint2, drenageName)
        Dim drLink2 = drenageLinks.Add(drPoint2, drPoint3, drenageName)
        Dim drLink3 = drenageLinks.Add(drPoint3, drPoint4, drenageName)
        Dim drLink4 = drenageLinks.Add(drPoint1, drPoint4, drenageName)
        'create shape for gravel layer
        Dim drShapeName As String = "" 'subAsName & "_" & drenageName '& CStr(grNumber)
        Dim drShape = drenageShapes.Add(drLink1, drLink2, drLink3, drLink4, drenageName)
        '----------------------------
        'sand layer
        '----------------------------
        'создание коллекций для точек и связей
        Dim sandPoints As PointCollection = corridorState.Points
        Dim sandLinks As LinkCollection = corridorState.Links
        Dim sandShapes As ShapeCollection = corridorState.Shapes

        Dim sandPoint1 As Point
        Dim sandPoint2 As Point
        Dim sandPoint3 As Point
        Dim sandPoint4 As Point

        Dim sandLink1 As Link
        Dim sandLink2 As Link
        Dim sandLink3 As Link
        Dim sandLink4 As Link

        Dim sandShape As Shape
        'имена точек при построении сечения
        Dim sandPointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 1
        Dim sandPointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 2
        Dim sandPointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 3
        Dim sandPointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 4
        'имена связей при построении сечения
        'Dim sandLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandUpBase"
        'Dim sandLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandDownBase"
        'создание точек контура песка
        sandPoint1 = sandPoints.Add(drPoint4.Offset, drPoint4.Elevation, sandPointName1)
        sandPoint2 = sandPoints.Add(drPoint3.Offset, drPoint3.Elevation, sandPointName2)
        'проверка наличия цели для отступа(ширины стены)
        If hasTargetOffset Then

            sandPoint3 = sandPoints.Add(soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(soilWidth, sandPoint1.Elevation, sandPointName4)
        Else
            sandPoint3 = sandPoints.Add(drPoint2.Offset + soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(drPoint1.Offset + soilWidth, sandPoint1.Elevation, sandPointName4)
        End If
        'создание линий контура песка
        sandLink1 = sandLinks.Add(sandPoint1, sandPoint2, "")
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, "")
        sandLink3 = sandLinks.Add(sandPoint3, sandPoint4, soilName)
        sandLink4 = sandLinks.Add(sandPoint4, sandPoint1, "")
        ' создание заполнения контура песка
        sandShape = sandShapes.Add(sandLink1, sandLink2, sandLink3, sandLink4, soilName)
        '-------------------------
        'geotextile
        '-------------------------
        Dim geotxtPoints As PointCollection = corridorState.Points
        Dim geotxtLinks As LinkCollection = corridorState.Links
        'объявим точки для геотекстиля
        Dim geotextilePoint1 As Point
        Dim geotextilePoint2 As Point
        Dim geotextilePoint3 As Point
        Dim geotextilePoint4 As Point
        'объявим связи для геотекстиля
        Dim geotextileLink1 As Link
        Dim geotextileLink2 As Link
        Dim geotextileLink3 As Link

        Dim geotextilePointName1 As String = "" ' subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 1
        Dim geotextilePointName2 As String = "" ' subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 2
        Dim geotextilePointName3 As String = "" ' subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 3
        Dim geotextilePointName4 As String = "" ' subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 4
        'проверка наличия сверху слоя с дренажом
        If isFirstlayer Then
            geotextilePoint1 = geotxtPoints.Add(sandPoint1.Offset + gtxtOffset, sandPoint1.Elevation, geotextilePointName1)
        Else
            geotextilePoint1 = geotxtPoints.Add(sandPoint1.Offset + gtxtOffset + dOffset, sandPoint1.Elevation, geotextilePointName1)
        End If
        geotextilePoint2 = geotxtPoints.Add(sandPoint1.Offset, sandPoint1.Elevation, geotextilePointName2)
        geotextilePoint3 = geotxtPoints.Add(sandPoint2.Offset, sandPoint2.Elevation, geotextilePointName3)
        geotextilePoint4 = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset, sandPoint2.Elevation, geotextilePointName4)

        'Dim geotextileLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileUp"
        'Dim geotextileLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileDown"

        geotextileLink1 = geotxtLinks.Add(geotextilePoint1, geotextilePoint2, gtxtName)
        geotextileLink2 = geotxtLinks.Add(geotextilePoint2, geotextilePoint3, gtxtName)
        geotextileLink3 = geotxtLinks.Add(geotextilePoint3, geotextilePoint4, gtxtName)

        If isFirstlayer Then 'создание дренажной трубы и геомембраны в основании пристеночного дренажа
            Dim dPoint As Point
            createPipeAxis(corridorState, pipeStep, pipeSlope, pointToInsert, dPoint, pipeDiameter, flipValue)
            createPipe(corridorState, dPoint, pipeDiameter)
            createGeomembrane(corridorState, pointToInsert, pipeDiameter, drenageWidth, layerHeight, drenageSlope, flipValue)
        End If
    End Sub
    'создание слоя засыпки с пристеночным дренажом самого верхнего слоя (с возможностью задать перехлест геотекстиля с нижне лежащим слоем) 
    Private Sub createLastDrenageLayer(ByVal corridorState As CorridorState,
                                   ByVal drenageName As String,
                                   ByVal soilName As String,
                                   ByVal gtxtName As String,
                                   ByVal soilWidth As Double,
                                   ByVal layerHeight As Double, 'высота стандартного слоя
                                   ByVal layerHeightBack As Double,
                                   ByVal drenageWidth As Double,
                                   ByVal drenageSlope As Double,
                                   ByVal flipValue As Double,
                                   ByVal pointToInsert As PointInMem,
                                   ByVal geotxtOverlap As Double,
                                   ByVal hasTargetOffset As Boolean,
                                   ByVal hasTargetElev As Boolean,
                                   ByVal isFirstlayer As Boolean,
                                   ByVal lastHeight As Double 'высота последнего слоя
                                   )
        'вычисляем вспомогательные параметры
        'Dim faceSlope As Double = faceAngle * (Math.PI / 180)
        'Dim fOffset As Double = lastHeight * Math.Tan(faceSlope) * flipValue 'last layer FaceOffset
        Dim dOffset As Double = lastHeight * drenageSlope * flipValue 'layer DrenageOffset
        Dim gOffset As Double = drenageWidth * flipValue 'gravel offset
        Dim gtxtOffset = geotxtOverlap * flipValue 'перехлест геотекстиля
        Dim gtxtLow As Double = (layerHeight * drenageSlope) * flipValue
        Dim gtxtTop As Double = (drenageWidth + dOffset) * flipValue
        ' Dim fOffsetLow As Double = layerHeight * Math.Tan(faceSlope) * flipValue 'layer FaceOffset
        '----------------------------
        'drenage layer
        '----------------------------
        'объявляем коллекции элементов
        Dim drenagePoints As PointCollection = corridorState.Points
        Dim drenageLinks As LinkCollection = corridorState.Links
        Dim drenageShapes As ShapeCollection = corridorState.Shapes
        'имена точек щебня
        Dim drPointName1 As String = "" ' subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 1
        Dim drPointName2 As String = "Пристеночный дренаж верх по лицевой грани" ' subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 2
        Dim drPointName3 As String = "Пристеночный дренаж верх по засыпке" ' subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 3
        Dim drPointName4 As String = "Пристеночный дренаж низ по засыпке" ' subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 4
        'строим слой по точкам
        Dim drPoint1 = drenagePoints.Add(pointToInsert.Offset, pointToInsert.Elevation, drPointName1)
        Dim drPoint2 = drenagePoints.Add(drPoint1.Offset, drPoint1.Elevation + lastHeight, drPointName2)
        Dim drPoint3 = drenagePoints.Add(drPoint1.Offset + gOffset + dOffset, drPoint2.Elevation, drPointName3)
        Dim drPoint4 = drenagePoints.Add(pointToInsert.Offset + gOffset, pointToInsert.Elevation, drPointName4)
        'declare description for links
        'Dim drLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "gravelUp"
        'Dim drLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "Drenagelayer"
        'create links of gravel layer
        Dim drLink1 = drenageLinks.Add(drPoint1, drPoint2, drenageName)
        Dim drLink2 = drenageLinks.Add(drPoint2, drPoint3, "Верх дренажа")
        Dim drLink3 = drenageLinks.Add(drPoint3, drPoint4, drenageName)
        Dim drLink4 = drenageLinks.Add(drPoint1, drPoint4, drenageName)
        'create shape for gravel layer
        'Dim drShapeName As String = subAsName & "_" & drenageName '& CStr(grNumber)
        Dim drShape = drenageShapes.Add(drLink1, drLink2, drLink3, drLink4, drenageName)
        '----------------------------
        'sand layer
        '----------------------------
        'создание коллекций для точек и связей
        Dim sandPoints As PointCollection = corridorState.Points
        Dim sandLinks As LinkCollection = corridorState.Links
        Dim sandShapes As ShapeCollection = corridorState.Shapes

        Dim sandPoint1 As Point
        Dim sandPoint2 As Point
        Dim sandPoint3 As Point
        Dim sandPoint4 As Point

        Dim sandLink1 As Link
        Dim sandLink2 As Link
        Dim sandLink3 As Link
        Dim sandLink4 As Link

        Dim sandShape As Shape
        'имена точек при построении сечения
        Dim sandPointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 1
        Dim sandPointName2 As String = "Засыпка по лицевой стороне" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 2
        Dim sandPointName3 As String = "Засыпка по тыльной стороне" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 3
        Dim sandPointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 4
        'имена связей при построении сечения
        ' Dim sandLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandUpBase"
        ' Dim sandLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandDownBase"
        'создание точек контура песка
        sandPoint1 = sandPoints.Add(drPoint4.Offset, drPoint4.Elevation, sandPointName1)
        sandPoint2 = sandPoints.Add(drPoint3.Offset, drPoint3.Elevation, sandPointName2)
        'проверка наличия цели для отступа(ширины стены)
        If hasTargetOffset Then
            sandPoint3 = sandPoints.Add(soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(soilWidth, sandPoint1.Elevation, sandPointName4)
        Else
            sandPoint3 = sandPoints.Add(drPoint2.Offset + soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(drPoint1.Offset + soilWidth, sandPoint1.Elevation, sandPointName4)
        End If
        If hasTargetElev Then
            If layerHeightBack >= sandPoint1.Elevation Then
                sandPoint3.Elevation = layerHeightBack
            Else
                sandPoint3.Elevation = sandPoint2.Elevation
            End If
        End If
        'создание линий контура песка
        sandLink1 = sandLinks.Add(sandPoint1, sandPoint2, "")
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, "Верх дренирующего грунта")
        sandLink3 = sandLinks.Add(sandPoint3, sandPoint4, soilName)
        sandLink4 = sandLinks.Add(sandPoint4, sandPoint1, "")
        ' создание заполнения контура песка
        sandShape = sandShapes.Add(sandLink1, sandLink2, sandLink3, sandLink4, soilName)
        '-------------------------
        'geotextile
        '-------------------------
        Dim geotxtPoints As PointCollection = corridorState.Points
        Dim geotxtLinks As LinkCollection = corridorState.Links
        'объявим точки для геотекстиля
        Dim geotextilePoint1 As Point
        Dim geotextilePoint2 As Point
        Dim geotextilePoint3 As Point
        Dim geotextilePoint4 As Point
        'Dim geotextilePoint5 As Point
        'Dim geotextilePoint6 As Point
        'объявим связи для геотекстиля
        Dim geotextileLink1 As Link
        Dim geotextileLink2 As Link
        Dim geotextileLink3 As Link
        'Dim geotextileLink4 As Link

        Dim geotextilePointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 1
        Dim geotextilePointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 2
        Dim geotextilePointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 3
        Dim geotextilePointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 4
        'Dim geotextilePointName5 As String = subAsName & "_" & "geotextile" & 5
        'Dim geotextilePointName6 As String = subAsName & "_" & "geotextile" & 6

        'проверка наличия сверху слоя с дренажом
        If isFirstlayer Then
            geotextilePoint1 = geotxtPoints.Add(sandPoint1.Offset + gtxtOffset, sandPoint1.Elevation, geotextilePointName1)
        Else
            geotextilePoint1 = geotxtPoints.Add(sandPoint1.Offset + (gtxtOffset + gtxtLow), sandPoint1.Elevation, geotextilePointName1)
        End If
        geotextilePoint2 = geotxtPoints.Add(sandPoint1.Offset, sandPoint1.Elevation, geotextilePointName2)
        geotextilePoint3 = geotxtPoints.Add(sandPoint2.Offset, sandPoint2.Elevation, geotextilePointName3)
        geotextilePoint4 = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset, sandPoint2.Elevation, geotextilePointName4)
        'geotextilePoint5 = geotxtPoints.Add(geotextilePoint4.Offset, geotextilePoint4.Elevation, geotextilePointName5)
        ' geotextilePoint6 = geotxtPoints.Add(geotextilePoint5.Offset - (gtxtOffset + gtxtTop), geotextilePoint5.Elevation, geotextilePointName6)


        '  Dim geotextileLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileUp"
        ' Dim geotextileLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileDown"
        ' Dim geotextileLinkName3 As String = subAsName & "_" & "geotextileTop"

        geotextileLink1 = geotxtLinks.Add(geotextilePoint1, geotextilePoint2, gtxtName)
        geotextileLink2 = geotxtLinks.Add(geotextilePoint2, geotextilePoint3, gtxtName)
        geotextileLink3 = geotxtLinks.Add(geotextilePoint3, geotextilePoint4, gtxtName)

        'geotextileLink4 = geotxtLinks.Add(geotextilePoint5, geotextilePoint6, geotextileLinkName3)
    End Sub
    'создание слоя засыпки дренирующим грунтом самого верхнего слоя
    Private Sub createLastNonDrenageLayer(ByVal corridorState As CorridorState,
                                      ByVal soilName As String,
                                      ByVal soilWidth As Double,
                                      ByVal layerHeight As Double,
                                      ByVal layerHeightBack As Double,
                                      ByVal flipValue As Double,
                                      ByVal pointToInsert As PointInMem,
                                      ByVal geotxtOverlap As Double,
                                      ByVal geotextileName As String,
                                      ByVal hasTargetOffset As Boolean,
                                      ByVal hasTargetElev As Boolean
                                      )
        '----------------------------
        'sand layer
        '----------------------------
        'создание коллекций для точек,связей и форм
        Dim sandPoints As PointCollection = corridorState.Points
        Dim sandLinks As LinkCollection = corridorState.Links
        Dim sandShapes As ShapeCollection = corridorState.Shapes

        Dim sandPoint1 As Point
        Dim sandPoint2 As Point
        Dim sandPoint3 As Point
        Dim sandPoint4 As Point

        Dim sandLink1 As Link
        Dim sandLink2 As Link
        Dim sandLink3 As Link
        Dim sandLink4 As Link

        Dim sandShape1 As Shape
        'имена точек при построении сечения
        Dim sandPointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 1
        Dim sandPointName2 As String = "Засыпка по лицевой стороне" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 2
        Dim sandPointName3 As String = "Засыпка по тыльной стороне" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 3
        Dim sandPointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 4
        'создание точек контура песка
        sandPoint1 = sandPoints.Add(pointToInsert.Offset, pointToInsert.Elevation, sandPointName1)
        sandPoint2 = sandPoints.Add(sandPoint1.Offset, sandPoint1.Elevation + layerHeight, sandPointName2)
        'проверка наличия цели для отступа(ширины стены)
        If hasTargetOffset Then
            sandPoint3 = sandPoints.Add(soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(soilWidth, sandPoint1.Elevation, sandPointName4)
        Else
            sandPoint3 = sandPoints.Add(sandPoint2.Offset + soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(sandPoint1.Offset + soilWidth, sandPoint1.Elevation, sandPointName4)
        End If
        'проверка наличия цели для отметки(тыльной точки стены)
        If hasTargetElev Then
            If layerHeightBack >= sandPoint1.Elevation Then
                sandPoint3.Elevation = layerHeightBack
            Else
                sandPoint3.Elevation = sandPoint2.Elevation
            End If
        End If
        'создание линий контура песка
        sandLink1 = sandLinks.Add(sandPoint1, sandPoint2, soilName)
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, "Верх дренирующего грунта")
        sandLink3 = sandLinks.Add(sandPoint3, sandPoint4, soilName)
        sandLink4 = sandLinks.Add(sandPoint4, sandPoint1, "")
        ' создание заполнения контура песка
        sandShape1 = sandShapes.Add(sandLink1, sandLink2, sandLink3, sandLink4, soilName)
        '-------------------------
        'geotextile
        '-------------------------
        'вычисляем вспомогательные параметры
        Dim gtxtOffset = geotxtOverlap * flipValue
        'создание коллекций для точек и связей
        Dim geotxtPoints As PointCollection = corridorState.Points
        Dim geotxtLinks As LinkCollection = corridorState.Links
        'объявим точки для геотекстиля
        Dim geotextilePoint1 As Point
        Dim geotextilePoint2 As Point
        Dim geotextilePoint3 As Point
        Dim geotextilePoint4 As Point
        'объявим связи для геотекстиля
        Dim geotextileLink1 As Link
        Dim geotextileLink2 As Link
        Dim geotextileLink3 As Link

        Dim geotextilePointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 1
        Dim geotextilePointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 2
        Dim geotextilePointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 3
        Dim geotextilePointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 4

        geotextilePoint1 = geotxtPoints.Add(sandPoint1.Offset + gtxtOffset, sandPoint1.Elevation, geotextilePointName1)
        geotextilePoint2 = geotxtPoints.Add(sandPoint1.Offset, sandPoint1.Elevation, geotextilePointName2)
        geotextilePoint3 = geotxtPoints.Add(sandPoint2.Offset + 0.001 * flipValue, sandPoint2.Elevation, geotextilePointName3)
        geotextilePoint4 = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset, sandPoint2.Elevation, geotextilePointName4)

        geotextileLink1 = geotxtLinks.Add(geotextilePoint1, geotextilePoint2, geotextileName)
        geotextileLink2 = geotxtLinks.Add(geotextilePoint2, geotextilePoint3, geotextileName)
        geotextileLink3 = geotxtLinks.Add(geotextilePoint3, geotextilePoint4, geotextileName)
    End Sub
    'создание слоя засыпки дренирующим грунтом самого верхнего слоя
    Private Sub createPipeAxis(ByVal corridorState As CorridorState, ByVal pipeStep As Double, ByVal pipeSlope As Double, ByVal insertPoint As PointInMem, ByRef axisPoint As Point, pipeDiam As Double, flip As Double)
        Dim dPointCollection As PointCollection
        dPointCollection = corridorState.Points
        'находим переменные задающие положение трубы в пространстве (вертикальное смещение)
        'максимально возможная отметка относительно нуля 
        Dim tH = pipeStep * pipeSlope
        'дельта отметки для рассматриваемого сечения
        Dim dH = ((corridorState.CurrentStation - corridorState.CurrentRegionStartStation) Mod 2 * pipeStep) * pipeSlope
        'направление в котором стоит откладывать дельту в текущем сечении
        Dim dir = Math.Sin(PI / 2 + ((corridorState.CurrentStation - corridorState.CurrentRegionStartStation) \ pipeStep) * PI)
        'определяем отметку

        Dim oPipeElev As Double
        oPipeElev = Math.Abs(tH - dH) + pipeDiam / 2

        axisPoint = dPointCollection.Add(insertPoint.Offset + (pipeDiam / 2 + 0.05) * flip, insertPoint.Elevation + oPipeElev, "Ось дренажной трубы")

    End Sub
    Private Sub createPipe(ByVal corridorState As CorridorState, cPoint As Point, pDiam As Double)
        Dim pipePoints As PointCollection
        pipePoints = corridorState.Points
        Dim pipeLinks As LinkCollection
        pipeLinks = corridorState.Links
        Dim pipeShapes As ShapeCollection
        pipeShapes = corridorState.Shapes
        Dim P1 As Point
        Dim P2 As Point
        Dim L1 As Link
        Dim S1 As Autodesk.Civil.DatabaseServices.Shape

        Dim i As Double = 0
        Dim circleStep = PI / 6
        Dim links As New List(Of Link)
        Do While i < 2 * PI
            If i <> 1.5 * PI Then
                P1 = pipePoints.Add(cPoint.Offset + Math.Cos(i) * pDiam / 2, cPoint.Elevation + Math.Sin(i) * pDiam / 2, "")
                P2 = pipePoints.Add(cPoint.Offset + Math.Cos(i + circleStep) * pDiam / 2, cPoint.Elevation + Math.Sin(i + circleStep) * pDiam / 2, "")
                L1 = pipeLinks.Add(P1, P2, "")
            Else
                P1 = pipePoints.Add(cPoint.Offset + Math.Cos(i) * pDiam / 2, cPoint.Elevation + Math.Sin(i) * pDiam / 2, "Низ дренажной трубы")
                P2 = pipePoints.Add(cPoint.Offset + Math.Cos(i + circleStep) * pDiam / 2, cPoint.Elevation + Math.Sin(i + circleStep) * pDiam / 2, "")
                L1 = pipeLinks.Add(P1, P2, "")
            End If
            links.Add(L1)
            i += circleStep
        Loop
        S1 = pipeShapes.Add(links.ToArray(), "Дренажная труба")
    End Sub
    Private Sub createGeomembrane(corridorState As CorridorState, ByVal insertPoint As PointInMem, pDiam As Double, drenageOffset As Double, layerHeight As Double, layerSlope As Double, flip As Double)

        Dim membranePoints As PointCollection
        membranePoints = corridorState.Points
        Dim membraneLinks As LinkCollection
        membraneLinks = corridorState.Links

        Dim membraneName As String = "Геомембрана"

        Dim P1 As Point
        Dim P2 As Point
        Dim P3 As Point
        Dim P4 As Point
        Dim L1 As Link
        Dim L2 As Link
        Dim L3 As Link

        Dim membraneLow = insertPoint.Elevation
        Dim membraneHeightFace = 0.3
        Dim layerTan = 1 / layerSlope

        P1 = membranePoints.Add(insertPoint.Offset + 0.001 * flip, insertPoint.Elevation + membraneHeightFace, "")
        P2 = membranePoints.Add(insertPoint.Offset, insertPoint.Elevation, "")
        P3 = membranePoints.Add(P2.Offset + drenageOffset * flip, P2.Elevation, "")
        P4 = membranePoints.Add(P3.Offset + layerHeight * layerSlope * flip, P3.Elevation + layerHeight, "")


        L1 = membraneLinks.Add(P1, P2, membraneName)
        L2 = membraneLinks.Add(P2, P3, membraneName)
        L3 = membraneLinks.Add(P3, P4, membraneName)
    End Sub
    Public Sub createPipeAddStations(tm As DBTransactionManager, corridorState As CorridorState, pipeStep As Double, pipeSlope As Double)
        Dim origin As New PointInMem
        Dim alignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, alignmentId, origin)
        'пробегаем по всей области и находи пикеты "скачка" блоков
        Dim startSt = corridorState.CurrentRegionStartStation
        Dim stateStep As Double = 0.001
        Dim endSt = corridorState.CurrentRegionEndStation
        Dim stationCurr = startSt
        Dim sectionsToAdd As New List(Of Double)
        Dim tH = pipeStep * pipeSlope
        Do While stationCurr < endSt
            Dim dH = ((stationCurr - corridorState.CurrentRegionStartStation) Mod pipeStep) * pipeSlope
            Dim remainder = tH - dH
            If Math.Abs(remainder) < 0.0001 Then
                sectionsToAdd.Add(stationCurr)
                stationCurr += 0.1
            End If
            stationCurr += stateStep
        Loop

        Dim corridor As Corridor
        corridor = tm.GetObject(corridorState.CurrentCorridorId, OpenMode.ForWrite)
        Dim baselines As BaselineCollection
        baselines = corridor.Baselines
        Dim baseline As Baseline
        For Each b As Baseline In baselines
            If corridorState.CurrentProfileId = b.ProfileId Then
                baseline = b
                Dim regs As BaselineRegionCollection
                regs = baseline.BaselineRegions
                For Each reg As BaselineRegion In regs
                    If reg.StartStation = corridorState.CurrentRegionStartStation Or reg.EndStation = corridorState.CurrentRegionEndStation Then
                        'очищаем дополнительные сечения
                        Dim settings = reg.AppliedAssemblySetting
                        Dim infos = settings.AdditionalAppliedAssemblies
                        For Each info In infos
                            Dim description = "доп.сечения дренажной трубы " + baseline.Name
                            If info.Description = description Then
                                reg.DeleteStation(info.Station)
                            End If
                        Next
                        'добавляем новые сечения 
                        Dim assemblyStations As Double()
                        assemblyStations = reg.AppliedAssemblies.Stations
                        'если в точке нет сечения - создаем дополнительное
                        Dim diff = sectionsToAdd.Except(assemblyStations)
                        For Each station In diff
                            Try
                                reg.AddStation(station, "доп.сечения дренажной трубы " + baseline.Name)
                            Catch

                            End Try
                        Next
                    End If
                Next
            End If
        Next
    End Sub
#End Region
#Region "Создание облицовки"
    'создание конструкции
    Public Sub createCladdingMB(ByVal corridorState As CorridorState, ByVal dWallHeight As Double, ByVal flipValue As Double, ByVal blockRows As Integer, ByVal blockVerticalStep As Double, ByVal delHeight As Double, ByVal levelingWidth As Double, ByVal origin As PointInMem, ByRef outputPoint As PointInMem)

        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        'создание облицовочных блоков

        'общая высота по облицовочным блокам
        'Dim totalHeight = blockRows * blockVerticalStep
        'точки вставки облицовочных блоков
        Dim newAddPoint As New PointInMem With {
        .Offset = 0,
        .Elevation = origin.Elevation
        }
        'точка вставки омоноличивания
        Dim levelingTopPoint As New PointInMem With {
            .Offset = cutL1 * flipValue,
            .Elevation = origin.Elevation + dWallHeight
        }
        Dim topPntAdd As New PointInMem With {
            .Offset = cutL1 * flipValue,
            .Elevation = origin.Elevation + blockRows * (dBlockHeight + delHeight)
            }
        levelingTop(corridorState, levelingTopPoint, topPntAdd, flipValue)
        'точка вывода середины первого блока
        outputPoint.Offset = newAddPoint.Offset + flipValue * (baseL2 + cutL1)
        outputPoint.Elevation = newAddPoint.Elevation
        Dim i As Integer = 1
        While i <= blockRows
            createBlock(corridorState, newAddPoint, flipValue)
            newAddPoint.Offset += dBlockOffset * flipValue
            newAddPoint.Elevation += blockVerticalStep
            i += 1
        End While
        'newAddPoint.Offset += cutL1 * flipValue
        'newAddPoint.Elevation += blockVerticalStep

    End Sub
    'создание облицовочного блока
    Public Sub createBlock(ByVal corridorState As CorridorState, addPoint As PointInMem, flip As Double)
        Dim blockPoints As PointCollection = corridorState.Points
        Dim blockLinks As LinkCollection = corridorState.Links
        Dim blockShapes As ShapeCollection = corridorState.Shapes

        Dim blockName As String = "Облицовочный блок МБ1"
        Dim blockF As String = "Лицевая грань облицовочного блока"
        Dim blockT As String = "Верхняя грань облицовочного блока"

        Dim blockPoint1 As Point
        Dim blockPoint2 As Point
        Dim blockPoint3 As Point
        Dim blockPoint4 As Point
        Dim blockPoint5 As Point
        Dim blockPoint6 As Point
        Dim blockPoint7 As Point
        Dim blockPoint8 As Point
        Dim blockPoint9 As Point
        Dim blockPoint10 As Point

        Dim blockLink1 As Link
        Dim blockLink2 As Link
        Dim blockLink3 As Link
        Dim blockLink4 As Link
        Dim blockLink5 As Link
        Dim blockLink6 As Link
        Dim blockLink7 As Link
        Dim blockLink8 As Link
        Dim blockLink9 As Link
        Dim blockLink10 As Link

        Dim blockShape1 As Autodesk.Civil.DatabaseServices.Shape

        blockPoint1 = blockPoints.Add(addPoint.Offset + (cutL1 + baseL2) * flip, addPoint.Elevation, "Точка раскладки облицовочных блоков")
        blockPoint2 = blockPoints.Add(blockPoint1.Offset - baseL2 * flip, blockPoint1.Elevation, "")
        blockPoint3 = blockPoints.Add(blockPoint2.Offset - cutL1 * flip, blockPoint2.Elevation + cutL1, "")
        blockPoint4 = blockPoints.Add(blockPoint3.Offset, blockPoint3.Elevation + faceHeight, "")
        blockPoint5 = blockPoints.Add(blockPoint4.Offset + cutL1 * flip, blockPoint4.Elevation + cutL1, "")
        blockPoint6 = blockPoints.Add(blockPoint5.Offset + baseL2 * flip, blockPoint5.Elevation, "")
        blockPoint7 = blockPoints.Add(blockPoint6.Offset + toothL1 * flip, blockPoint6.Elevation - toothH1, "")
        blockPoint8 = blockPoints.Add(blockPoint7.Offset + baseL1 * flip, blockPoint7.Elevation, "")
        blockPoint9 = blockPoints.Add(blockPoint8.Offset, blockPoint8.Elevation - dBlockHeight, "")
        blockPoint10 = blockPoints.Add(blockPoint9.Offset - baseL1 * flip, blockPoint9.Elevation, "")

        blockLink1 = blockLinks.Add(blockPoint1, blockPoint2, "")
        blockLink2 = blockLinks.Add(blockPoint2, blockPoint3, "")
        blockLink3 = blockLinks.Add(blockPoint3, blockPoint4, "")
        blockLink4 = blockLinks.Add(blockPoint4, blockPoint5, "")
        blockLink5 = blockLinks.Add(blockPoint5, blockPoint6, blockT)
        blockLink6 = blockLinks.Add(blockPoint6, blockPoint7, blockT)
        blockLink7 = blockLinks.Add(blockPoint7, blockPoint8, blockT)
        blockLink8 = blockLinks.Add(blockPoint8, blockPoint9, "")
        blockLink9 = blockLinks.Add(blockPoint9, blockPoint10, "")
        blockLink10 = blockLinks.Add(blockPoint10, blockPoint1, "")

        Dim linkCollect As Link() = {blockLink1, blockLink2, blockLink3, blockLink4, blockLink5, blockLink6, blockLink7, blockLink8, blockLink9, blockLink10}

        blockShape1 = blockShapes.Add(linkCollect, blockName)
        '----------------------------------------------
        'создание звена для подсчета площади облицовки
        Dim fP1 As Point = blockPoints.Add(addPoint.Offset, addPoint.Elevation, "")
        Dim fP2 As Point = blockPoints.Add(addPoint.Offset + 0.001 * flip, addPoint.Elevation + (cutL1 * 2 + faceHeight), "")
        Dim fL1 As Link = blockLinks.Add(fP1, fP2, blockF)
    End Sub
    'метод для создания выравнивающей ленты
    Public Sub levelingTop(ByVal corridorState As CorridorState,
                           ByVal topPoint As PointInMem,
                           ByVal lowPoint As PointInMem,
                           ByVal flip As Double)

        Dim levelPoints As PointCollection = corridorState.Points
        Dim levelLinks As LinkCollection = corridorState.Links
        Dim levelShapes As ShapeCollection = corridorState.Shapes

        Dim levTopName As String = "Выравнивающая лента"

        If topPoint.Elevation < lowPoint.Elevation Then
            topPoint.Elevation = lowPoint.Elevation
        End If

        Dim levPoint1 = levelPoints.Add(lowPoint.Offset, lowPoint.Elevation, "Низ выравнивающего слоя")
        Dim levPoint2 = levelPoints.Add(topPoint.Offset, topPoint.Elevation, "Верх выравнивающего слоя")
        Dim levPoint3 = levelPoints.Add(levPoint2.Offset + (baseL1 + baseL2 + toothL1) * flip, levPoint2.Elevation, "")
        Dim levPoint4 = levelPoints.Add(levPoint1.Offset + baseL2 * flip, levPoint1.Elevation, "")
        Dim levPoint5 = levelPoints.Add(levPoint4.Offset + toothL1 * flip, levPoint4.Elevation - toothH1, "")
        Dim levPoint6 = levelPoints.Add(levPoint5.Offset + baseL1 * flip, levPoint5.Elevation, "")

        Dim levLink1 = levelLinks.Add(levPoint1, levPoint2, levTopName)
        Dim levLink2 = levelLinks.Add(levPoint2, levPoint3, "")
        Dim levLink3 = levelLinks.Add(levPoint1, levPoint4, "")
        Dim levLink4 = levelLinks.Add(levPoint4, levPoint5, "")
        Dim levLink5 = levelLinks.Add(levPoint5, levPoint6, "")
        Dim levLink6 = levelLinks.Add(levPoint3, levPoint6, "")

        Dim levLinks As Link() = {levLink1, levLink2, levLink6, levLink5, levLink4, levLink3}

        Dim levShape = levelShapes.Add(levLinks, levTopName)
    End Sub
    'метод для добавления шага блока
    Public Sub createAddStations(tm As DBTransactionManager, corridorState As CorridorState, blockStep As Double, blockLength As Double, target As SlopeElevationTarget)
        Dim origin As New PointInMem
        Dim alignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, alignmentId, origin)
        'пробегаем по всей области и находим пикеты "скачка" блоков
        Dim startSt = corridorState.CurrentRegionStartStation
        Dim stateStep As Double = 0.0001
        Dim endSt = corridorState.CurrentRegionEndStation
        Dim stationCurr = startSt + blockLength / 2
        Dim sectionsToAdd As New List(Of Double)
        Dim sectionsToAddStep As New List(Of Double)
        Dim sliseStep = blockLength / 2
        Do While stationCurr < endSt
            Dim wallHeight = target.GetElevation(alignmentId, stationCurr) - origin.Elevation
            Dim remainder = wallHeight Mod blockStep
            If Math.Abs(remainder) < 0.0001 Then 'уточнение расстояния до вертикального скачка кратное длине половины облицовочного блока
                Dim rem1 = (stationCurr - startSt) Mod sliseStep
                Dim backSlice = stationCurr - rem1
                Dim rem2 = sliseStep - rem1
                Dim frontSlice = stationCurr + rem2
                Dim backH = target.GetElevation(alignmentId, backSlice)
                Dim frontH = target.GetElevation(alignmentId, frontSlice)
                If frontH <= backH Then
                    sectionsToAdd.Add(backSlice)
                    sectionsToAddStep.Add(backSlice + 0.001)
                    stationCurr = backSlice + sliseStep + 0.001
                Else
                    sectionsToAdd.Add(frontSlice)
                    sectionsToAddStep.Add(frontSlice + 0.001)
                    stationCurr = frontSlice + 0.001
                End If
            End If
            stationCurr += stateStep
        Loop

        Dim corridor As Corridor
        corridor = tm.GetObject(corridorState.CurrentCorridorId, OpenMode.ForWrite)
        Dim baselines As BaselineCollection
        baselines = corridor.Baselines
        Dim baseline As Baseline
        For Each b As Baseline In baselines
            If corridorState.CurrentProfileId = b.ProfileId Then
                baseline = b
                Dim regs As BaselineRegionCollection
                regs = baseline.BaselineRegions
                For Each reg As BaselineRegion In regs
                    If reg.StartStation = corridorState.CurrentRegionStartStation Or reg.EndStation = corridorState.CurrentRegionEndStation Then
                        'очищаем дополнительные сечения
                        Dim settings = reg.AppliedAssemblySetting
                        Dim infos = settings.AdditionalAppliedAssemblies
                        For Each info In infos
                            Dim description1 = "доп.сечения облицовочных блоков " + baseline.Name
                            If info.Description = description1 Then
                                reg.DeleteStation(info.Station)
                            End If
                            Dim description2 = "скачок облицовки " + baseline.Name
                            If info.Description = description2 Then
                                reg.DeleteStation(info.Station)
                            End If
                        Next
                        'добавляем новые сечения 
                        Dim assemblyStations As Double()
                        assemblyStations = reg.AppliedAssemblies.Stations
                        'если в точке нет сечения - создаем дополнительное
                        Dim diff1 = sectionsToAdd.Except(assemblyStations)
                        Dim diff2 = sectionsToAddStep.Except(assemblyStations)
                        For Each station In diff1
                            Try
                                reg.AddStation(station, "доп.сечения облицовочных блоков " + baseline.Name)
                            Catch

                            End Try
                        Next
                        For Each station In diff2
                            Try
                                reg.AddStation(station, "скачок облицовки " + baseline.Name)
                            Catch

                            End Try
                        Next
                    End If
                Next
            End If
        Next
    End Sub
    'условие для пересчета высоты облицовки (создание ступени)
    Public Function isStep(tm As DBTransactionManager, corridorState As CorridorState, stationCurr As Double)
        Dim result As Boolean = False
        Dim corridor As Corridor
        corridor = tm.GetObject(corridorState.CurrentCorridorId, OpenMode.ForWrite)
        Dim baselines As BaselineCollection
        baselines = corridor.Baselines
        Dim baseline As Baseline
        For Each b As Baseline In baselines
            If corridorState.CurrentProfileId = b.ProfileId Then
                baseline = b
                Dim regs As BaselineRegionCollection
                regs = baseline.BaselineRegions
                For Each reg As BaselineRegion In regs
                    If reg.StartStation = corridorState.CurrentRegionStartStation Or reg.EndStation = corridorState.CurrentRegionEndStation Then
                        'получаем свойства доп сечений
                        Dim settings = reg.AppliedAssemblySetting
                        Dim infos = settings.AdditionalAppliedAssemblies
                        For Each info In infos
                            Dim description = "скачок облицовки " + baseline.Name
                            If info.Description = description And stationCurr = info.Station Then
                                result = True
                            End If
                        Next
                    End If
                Next
            End If
        Next
        Return result
    End Function
    Public Sub createAddStationsForProfile(tm As DBTransactionManager, corridorState As CorridorState, target As SlopeElevationTarget)
        Dim origin As New PointInMem
        Dim alignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, alignmentId, origin)
        'пробегаем по всей области и находим пикеты "скачка" блоков
        'Dim startSt = corridorState.CurrentRegionStartStation
        'Dim stateStep As Double = 0.001
        'Dim endSt = corridorState.CurrentRegionEndStation
        'Dim stationCurr = startSt + blockLength / 2
        Dim sectionsToAdd As New List(Of Double)
        Dim sectionsToAddStep As New List(Of Double)
        'Dim sliseStep = blockLength / 2
        'Do While stationCurr < endSt
        '    Dim wallHeight = target.GetElevation(alignmentId, stationCurr) - origin.Elevation
        '    Dim remainder = wallHeight Mod blockStep
        '    If Math.Abs(remainder) < 0.001 Then
        '        Dim rem1 = stationCurr Mod sliseStep
        '        Dim backSlice = stationCurr - rem1
        '        Dim rem2 = sliseStep - rem1
        '        Dim frontSlice = stationCurr + rem2
        '        Dim backH = target.GetElevation(alignmentId, backSlice)
        '        Dim frontH = target.GetElevation(alignmentId, frontSlice)
        '        If frontH <= backH Then
        '            sectionsToAdd.Add(backSlice)
        '            sectionsToAddStep.Add(backSlice + 0.001)
        '        Else
        '            sectionsToAdd.Add(frontSlice)
        '            sectionsToAddStep.Add(frontSlice + 0.001)
        '        End If
        '        stationCurr += sliseStep
        '    End If
        '    stationCurr += stateStep
        'Loop

        Dim profileH As Autodesk.Civil.DatabaseServices.Profile = tm.GetObject(target.TargetId, OpenMode.ForRead)

        Dim corridor As Corridor
        corridor = tm.GetObject(corridorState.CurrentCorridorId, OpenMode.ForWrite)
        Dim baselines As BaselineCollection
        baselines = corridor.Baselines
        Dim baseline As Baseline
        For Each b As Baseline In baselines
            If corridorState.CurrentProfileId = b.ProfileId Then
                baseline = b
                Dim regs As BaselineRegionCollection
                regs = baseline.BaselineRegions
                For Each reg As BaselineRegion In regs
                    If reg.StartStation = corridorState.CurrentRegionStartStation Or reg.EndStation = corridorState.CurrentRegionEndStation Then
                        'очищаем дополнительные сечения
                        Dim settings = reg.AppliedAssemblySetting
                        Dim infos = settings.AdditionalAppliedAssemblies
                        For Each info In infos
                            Dim description1 = "доп.сечения облицовочных блоков " + baseline.Name
                            If info.Description = description1 Then
                                reg.DeleteStation(info.Station)
                            End If
                            Dim description2 = "скачок облицовки " + baseline.Name
                            If info.Description = description2 Then
                                reg.DeleteStation(info.Station)
                            End If
                        Next

                        Dim pEnts = profileH.Entities
                        For Each ent In pEnts
                            If ent.StartElevation <> ent.EndElevation And ent.StartStation > reg.StartStation And ent.StartStation < reg.EndStation Then
                                sectionsToAdd.Add(ent.StartStation)
                                sectionsToAddStep.Add(ent.EndStation)
                            End If
                        Next
                        'добавляем новые сечения

                        'Dim assemblyStations As Double()
                        'assemblyStations = reg.AppliedAssemblies.Stations
                        'если в точке нет сечения - создаем дополнительное
                        'Dim diff1 = sectionsToAdd.Except(assemblyStations)
                        'Dim diff2 = sectionsToAddStep.Except(assemblyStations)
                        For Each station In sectionsToAdd
                            Try
                                reg.AddStation(station, "доп.сечения облицовочных блоков " + baseline.Name)
                            Catch

                            End Try
                        Next
                        For Each station In sectionsToAddStep
                            Try
                                reg.AddStation(station, "скачок облицовки " + baseline.Name)
                            Catch

                            End Try
                        Next
                    End If
                Next
            End If
        Next
    End Sub
    'метод для определения пикета с первой в данной области ступенью(необходимо для проверки при понижении стены)
    Public Sub firstStepAtCurrRegion(tm As DBTransactionManager, corridorState As CorridorState, ByRef station As Double)
        Dim corridor As Corridor
        corridor = tm.GetObject(corridorState.CurrentCorridorId, OpenMode.ForWrite)
        Dim baselines As BaselineCollection
        baselines = corridor.Baselines
        Dim baseline As Baseline
        Dim firstStat As Double
        For Each b As Baseline In baselines
            If corridorState.CurrentProfileId = b.ProfileId Then
                baseline = b
                Dim regs As BaselineRegionCollection
                regs = baseline.BaselineRegions
                For Each reg As BaselineRegion In regs
                    If reg.StartStation = corridorState.CurrentRegionStartStation Or reg.EndStation = corridorState.CurrentRegionEndStation Then
                        'находим необходимое сечение
                        Dim settings = reg.AppliedAssemblySetting
                        Dim infos = settings.AdditionalAppliedAssemblies
                        For Each info In infos
                            Dim description = "скачок облицовки " + baseline.Name
                            If info.Description = description Then
                                firstStat = info.Station
                                Exit For
                            End If
                        Next
                    End If
                Next
            End If
        Next
        station = firstStat
    End Sub
#End Region

End Class


