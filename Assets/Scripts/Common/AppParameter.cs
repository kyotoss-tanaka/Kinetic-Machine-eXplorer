using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using UnityEngine;

namespace Parameters
{
    public enum RobotType
    {
        /// <summary>
        /// ���c�p������(3��)
        /// </summary>
        MPS2_3AS,
        /// <summary>
        /// ���c�p������(4��)
        /// </summary>
        MPS2_4AS,
        /// <summary>
        /// �ϑ��p������
        /// </summary>
        MPX_PI,
        /// <summary>
        /// ��d�p������
        /// </summary>
        YF03N4,
        /// <summary>
        /// ��d6��
        /// </summary>
        RS007L,
        /// <summary>
        /// ����`
        /// </summary>
        UNDEFINED
    }

    [Serializable]
    public class PostgresSetting
    {
        public int No { get; set; }
        public int Type { get; set; }
        public int Cycle { get; set; }
        public string Server { get; set; }
        public int Port { get; set; }
        public string Database { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public int ClientMode { get; set; }
        public string Name
        {
            get
            {
                return Server + ":" + Port;
            }
        }
        public bool isClientMode
        {
            get
            {
                return ClientMode == 1;
            }
        }
        public bool isPostgres
        {
            get
            {
                return Type == 0;
            }
        }
        public bool isMongo
        {
            get
            {
                return Type == 1;
            }
        }
        public bool isMqtt
        {
            get
            {
                return Type == 2;
            }
        }
        public bool isInner
        {
            get
            {
                return Type == 3;
            }
        }
    }


    [Serializable]
    public class DataExchangeSetting
    {
        /// <summary>
        /// DB�ԍ�
        /// </summary>
        public int dbNo { get; set; }
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// �f�[�^�ݒ�
        /// </summary>
        public List<DataEx> datas { get; set; }
    }

    public class DataEx
    {
        /// <summary>
        /// �����l
        /// </summary>
        public int initValue { get; set; }
        /// <summary>
        /// ���̓f�[�^
        /// </summary>
        public string input { get; set; }
        /// <summary>
        /// �o�̓f�[�^
        /// </summary>
        public string output { get; set; }
        /// <summary>
        /// ���������t���O
        /// </summary>
        public bool isInit
        {
            get
            {
                return (input == null) || (input == "");
            }
        }
    }

    [Serializable]
    public class UnitSetting
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string Database;
        /// <summary>
        /// DB�ԍ�
        /// </summary>
        public int dbNo { get; set; }
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ���j�b�g��
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// �Փ˂���
        /// </summary>
        public int collision { get; set; }
        /// <summary>
        /// �O���[�v�I�u�W�F�N�g
        /// </summary>
        public string group { get; set; }
        /// <summary>
        /// �e�I�u�W�F�N�g
        /// </summary>
        public string parent { get; set; }
        /// <summary>
        /// �q�I�u�W�F�N�g��
        /// </summary>
        public List<UnitChildren> children { get; set; }
        /// <summary>
        /// �q�I�u�W�F�N�g
        /// </summary>
        public List<GameObject> childrenObject;
        /// <summary>
        /// ����ݒ�
        /// </summary>
        public UnitActionSetting actionSetting;
        /// <summary>
        /// ���{�b�g�ݒ�
        /// </summary>
        public RobotSetting robotSetting;
        /// <summary>
        /// ���[�N�����ݒ�
        /// </summary>
        public WorkCreateSetting workSetting;
        /// <summary>
        /// ���[�N�����ݒ�
        /// </summary>
        public WorkDeleteSetting workDeleteSetting;
        /// <summary>
        /// �Z���T�ݒ�
        /// </summary>
        public List<SensorSetting> sensorSettings;
        /// <summary>
        /// �z���ݒ�
        /// </summary>
        public SuctionSetting suctionSetting;
        /// <summary>
        /// ���̌`��ݒ�
        /// </summary>
        public ShapeSetting shapeSetting;
        /// <summary>
        /// �X�C�b�`�ݒ�
        /// </summary>
        public SwitchSetting switchSetting;
        /// <summary>
        /// �V�O�i���^���[�ݒ�
        /// </summary>
        public SignalTowerSetting towerSetting;
        /// <summary>
        /// ����I�u�W�F�N�g
        /// </summary>
        public GameObject moveObject = null;
        /// <summary>
        /// ���j�b�g�I�u�W�F�N�g
        /// </summary>
        public GameObject unitObject { get; set; }
        /// <summary>
        /// �Փ˂���
        /// </summary>
        public bool isCollision
        {
            get
            {
                return collision == 1;
            }
        }
    }

    [Serializable]
    public class UnitChildren
    {
        public string name { get; set; }
        public string group { get; set; }
    }

    [Serializable]
    public class UnitActionSetting
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ���j�b�g��
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// ���샂�[�h 0:���� 1:��] 2:�O��(����) 3:�O��(��])
        /// </summary>
        public int mode { get; set; }
        /// <summary>
        /// ���쎲 0:X 1:Y 2:Z
        /// </summary>
        public int axis { get; set; }
        /// <summary>
        /// �����x�ݒ� 0:�����x(G) 1:����
        /// </summary>
        public int acl { get; set; }
        /// <summary>
        /// ����^�O
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// �ʐM�x�ꎞ��
        /// </summary>
        public int delay { get; set; }
        /// <summary>
        /// �T�C�N������
        /// </summary>
        public int cycle { get; set; }
        /// <summary>
        /// ����ݒ�
        /// </summary>
        public List<UnitAction> actions { get; set; }

        public bool isInternal
        {
            get
            {
                return mode == 0 || mode == 1;
            }
        }
        public bool isExternal
        {
            get
            {
                return mode == 2 || mode == 3;
            }
        }
        public bool isRobo
        {
            get
            {
                return mode == 4;
            }
        }
        public bool isPlanarMotor
        {
            get
            {
                return mode == 5;
            }
        }
        public bool isConveyer
        {
            get
            {
                return mode == 6;
            }
        }
    }

    [Serializable]
    public class UnitAction
    {
        /// <summary>
        /// �g���K�^�C�~���O
        /// </summary>
        public int trg { get; set; }
        /// <summary>
        /// �ڕW�ʒu
        /// </summary>
        public int target { get; set; }
        /// <summary>
        /// �I�t�Z�b�g
        /// </summary>
        public int offset { get; set; }
        /// <summary>
        /// ����
        /// </summary>
        public int dir { get; set; }
        /// <summary>
        /// �X�g���[�N
        /// </summary>
        public float stroke { get; set; }
        /// <summary>
        /// ���쎞��
        /// </summary>
        public float time { get; set; }
        /// <summary>
        /// �����ݒ�
        /// </summary>
        public float acl { get; set; }
        /// <summary>
        /// �����ݒ�
        /// </summary>
        public float dcl { get; set; }
        /// <summary>
        /// �J�n�g���KI/O
        /// </summary>
        public string start { get; set; }
        /// <summary>
        /// ����I/O
        /// </summary>
        public string end { get; set; }
        /// <summary>
        /// �p���t���O
        /// </summary>
        public bool isContinue { get; set; }
        /// <summary>
        /// �p�����쎞�̒�~����
        /// </summary>
        public float stop { get; set; }
        /// <summary>
        /// �ڕW���W
        /// </summary>
        [JsonIgnore]
        public Vector3 targetPos { get; set; }
        /// <summary>
        /// ���x
        /// </summary>
        [JsonIgnore]
        public float velocity { get; set; }
        /// <summary>
        /// ��������
        /// </summary>
        [JsonIgnore]
        public float aclTime { get; set; }
        /// <summary>
        /// ��������
        /// </summary>
        [JsonIgnore]
        public float dclTime { get; set; }
        /// <summary>
        /// �����x
        /// </summary>
        [JsonIgnore]
        public float aclVal { get; set; }
        /// <summary>
        /// �����x
        /// </summary>
        [JsonIgnore]
        public float dclVal { get; set; }
        /// <summary>
        /// �f�[�^�ύX�t���O
        /// </summary>
        [JsonIgnore]
        public bool isChanged { get; set; }
    }

    [Serializable]
    public class HiddenUnit
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ���j�b�g��
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// �e��
        /// </summary>
        public string parent { get; set; }
        /// <summary>
        /// ���[�h
        /// </summary>
        public int mode { get; set; }
        /// <summary>
        /// �����t���O
        /// </summary>
        public int disable { get; set; }
        /// <summary>
        /// �L��
        /// </summary>
        public bool isEnable
        {
            get
            {
                return disable == 0;
            }
        }

    }

    [Serializable]
    public class InnerProcessSetting
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// �^�O��
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// �T�C�N��
        /// </summary>
        public decimal cycle { get; set; }
        /// <summary>
        /// ON�^�C�~���O
        /// </summary>
        public decimal onTiming { get; set; }
        /// <summary>
        /// OFF�^�C�~���O
        /// </summary>
        public decimal offTiming { get; set; }
    }

    [Serializable]
    public class ChuckUnitSetting
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ���j�b�g��
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// �`���b�N���j�b�g��
        /// </summary>
        public List<ChuckUnit> children { get; set; }

    }

    [Serializable]
    public class ChuckUnit
    {
        public string name { get; set; }
        public int offset { get; set; }
        public int dir { get; set; }
        [JsonIgnore]
        public UnitSetting setting { get; set; }
    }

    [Serializable]
    public class RobotSetting
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ���j�b�g��
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// ���{�b�g�^�C�v
        /// </summary>
        public string type { get; set; }
        /// <summary>
        /// �w�b�h���j�b�g
        /// </summary>
        public string head { get; set; }
        /// <summary>
        /// �`���b�N���j�b�g��
        /// </summary>
        public List<string> tags { get; set; }
        /// <summary>
        /// �w�b�h���j�b�g�ݒ�
        /// </summary>
        public UnitSetting headUnit { get; set; }
        /// <summary>
        /// ���{�b�g�^�C�v
        /// </summary>
        public RobotType robo
        {
            get
            {
                switch (type)
                {
                    case "MPS2-3AS":
                        return RobotType.MPS2_3AS;

                    case "MPS2-4AS":
                        return RobotType.MPS2_4AS;

                    case "MPX-PI":
                        return RobotType.MPX_PI;

                    case "YF03N4":
                        return RobotType.YF03N4;

                    case "RS007L":
                        return RobotType.RS007L;

                    default:
                        return RobotType.UNDEFINED;

                }
            }
        }
    }

    [Serializable]
    public class PlanarMotorSetting
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ���j�b�g��
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// ���j�A��
        /// </summary>
        public int count { get; set; }
        /// <summary>
        /// �I�t�Z�b�g(�ʒu)
        /// </summary>
        public List<float> offset_p { get; set; }
        /// <summary>
        /// �I�t�Z�b�g(�p�x)
        /// </summary>
        public List<float> offset_r { get; set; }
        /// <summary>
        /// ����(�ʒu)
        /// </summary>
        public List<int> dir_p { get; set; }
        /// <summary>
        /// ����(�p�x)
        /// </summary>
        public List<int> dir_r { get; set; }
        /// <summary>
        /// �ʒu�^�O��
        /// </summary>
        public List<string> tags_p { get; set; }
        /// <summary>
        /// �p�x�^�O��
        /// </summary>
        public List<string> tags_r { get; set; }
        /// <summary>
        /// �w�b�h���j�b�g
        /// </summary>
        public string head { get; set; }
        /// <summary>
        /// �w�b�h���j�b�g�ݒ�
        /// </summary>
        public UnitSetting headUnit { get; set; }
    }


    [Serializable]
    public class ConveyerSetting
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ���j�b�g��
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// ���쎲 0:X 1:Y 2:Z
        /// </summary>
        public int axis { get; set; }
        /// <summary>
        /// ����
        /// </summary>
        public int dir { get; set; }
        /// <summary>
        /// ���x
        /// </summary>
        public float spd { get; set; }
        /// <summary>
        /// ������
        /// </summary>
        public float force { get; set; }
        /// <summary>
        /// �Î~���C�W��
        /// </summary>
        public float staticFriction { get; set; }
        /// <summary>
        /// �����C�W��
        /// </summary>
        public float dynamicFriction { get; set; }
        /// <summary>
        /// ����^�O
        /// </summary>
        public string actTag { get; set; }
    }

    [Serializable]
    public class WorkCreateSetting
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ���j�b�g��
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// ���[�N��
        /// </summary>
        public string work { get; set; }
        /// <summary>
        /// �c���\
        /// </summary>
        public int grabbable { get; set; }
        /// <summary>
        /// �^�C�}�[
        /// </summary>
        public int timer { get; set; }
        /// <summary>
        /// �����T�C�N��
        /// </summary>
        public float cycle { get; set; }
        /// <summary>
        /// �����^�O
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// ��������
        /// </summary>
        public float alive { get; set; }
        /// <summary>
        /// �I�t�Z�b�g(�ʒu)
        /// </summary>
        public List<float> pos { get; set; }
        /// <summary>
        /// �I�t�Z�b�g(�p�x)
        /// </summary>
        public List<float> rot { get; set; }

        public bool isGrabbable
        {
            get
            {
                return grabbable == 1;
            }
        }
        public bool isTimer
        {
            get
            {
                return timer == 1;
            }
        }
    }

    [Serializable]
    public class WorkDeleteSetting
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ���j�b�g��
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// �^�O��
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// ����
        /// </summary>
        public float distance { get; set; }
        /// <summary>
        /// �I�t�Z�b�g(�ʒu)
        /// </summary>
        public List<float> pos { get; set; }
    }

    [Serializable]
    public class SensorSetting
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ���j�b�g��
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// �Z���T����
        /// </summary>
        public int create { get; set; }
        /// <summary>
        /// ��
        /// </summary>
        public float width { get; set; }
        /// <summary>
        /// �����^�O
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// �I�t�Z�b�g(�ʒu)
        /// </summary>
        public List<float> pos { get; set; }
        /// <summary>
        /// �I�t�Z�b�g(�p�x)
        /// </summary>
        public List<float> rot { get; set; }

        public bool isCreate
        {
            get
            {
                return create == 1;
            }
        }
    }

    [Serializable]
    public class SuctionSetting
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ���j�b�g��
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// �����^�O
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// �Œ�(�ʒu)
        /// </summary>
        public List<int> pos_fixed { get; set; }
        /// <summary>
        /// �I�t�Z�b�g(�p�x)
        /// </summary>
        public List<int> rot_fixed { get; set; }
        /// <summary>
        /// �I�t�Z�b�g(�ʒu)
        /// </summary>
        public List<float> pos { get; set; }
        /// <summary>
        /// �I�t�Z�b�g(�p�x)
        /// </summary>
        public List<float> rot { get; set; }
    }

    [Serializable]
    public class ShapeSetting
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ���j�b�g��
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// �`
        /// </summary>
        public List<UnitShape> datas { get; set; }
    }

    [Serializable]
    public class UnitShape
    {
        /// <summary>
        /// ���S�_
        /// </summary>
        public List<float> center { get; set; }
        /// <summary>
        /// �T�C�Y
        /// </summary>
        public List<float> size { get; set; }
    }

    [Serializable]
    public class SwitchSetting
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ���j�b�g��
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// �F
        /// </summary>
        public string color { get; set; }
        /// <summary>
        /// �^�O��
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// �I���^�l�C�g
        /// </summary>
        public bool alternate { get; set; }
        /// <summary>
        /// ���[�h
        /// </summary>
        public int mode { get; set; }
        /// <summary>
        /// �I�t�Z�b�g(�ʒu)
        /// </summary>
        public List<float> pos { get; set; }
        /// <summary>
        /// �I�t�Z�b�g(�p�x)
        /// </summary>
        public List<float> rot { get; set; }
    }

    [Serializable]
    public class SignalTowerSetting
    {
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ���j�b�g��
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// �^���[�^�C�v
        /// </summary>
        public int type { get; set; }
        /// <summary>
        /// �^�O��
        /// </summary>
        public string red { get; set; }
        /// <summary>
        /// �^�O��
        /// </summary>
        public string yellow { get; set; }
        /// <summary>
        /// �^�O��
        /// </summary>
        public string green { get; set; }
        /// <summary>
        /// �^�O��
        /// </summary>
        public string blue { get; set; }
        /// <summary>
        /// �^�O��
        /// </summary>
        public string white { get; set; }
        /// <summary>
        /// �I�t�Z�b�g(�ʒu)
        /// </summary>
        public List<float> pos { get; set; }
        /// <summary>
        /// �I�t�Z�b�g(�p�x)
        /// </summary>
        public List<float> rot { get; set; }
    }

    [Serializable]
    public class DebugSetting
    {
        /// <summary>
        /// �f�[�^�x�[�X
        /// </summary>
        public string database { get; set; }
        /// <summary>
        /// �@��
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// �܂�Ԃ��p���̓^�O
        /// </summary>
        public string input { get; set; }
        /// <summary>
        /// �܂�Ԃ��p�o�̓^�O
        /// </summary>
        public string output { get; set; }
        /// <summary>
        /// �J�E���^�p���̓^�O
        /// </summary>
        public string inputCnt { get; set; }
        /// <summary>
        /// �J�E���^�p�o�̓^�O
        /// </summary>
        public string outputCnt { get; set; }
    }

    [Serializable]
    public class BuildConfig
    {
        public string name { get; set; }
        public string mechId { get; set; }
        public bool isRelease { get; set; }
        public bool isVR { get; set; }
        public bool isMR { get; set; }
    }
}