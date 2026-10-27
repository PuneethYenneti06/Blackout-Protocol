using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Camera : MonoBehaviour
{
    //Transform cameraPosition;
    public float sensx, sensy, xRotation, yRotation;
    public GameObject gameobj;
    // Start is called before the first frame update
    void Start()
    {
        gameobj = GameObject.Find("Capsule");
    }

    // Update is called once per frame
    void Update()
    {
        //transform.position = cameraPosition.position;
        float mousex = Input.GetAxisRaw("Mouse X") * Time.deltaTime * sensx;
        float mousey = Input.GetAxisRaw("Mouse Y") * Time.deltaTime * sensy;
        yRotation += mousex;
        xRotation -= mousey;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
        transform.rotation = Quaternion.Euler(xRotation, yRotation, 0);
        Vector3 abc = gameobj.transform.position;
        abc.y += 1.053f - 0.6f;
        transform.position = abc;
        /*if (Input.GetKeyDown("space"))
        {
            cameraPosition.velocity = new Vector3(cameraPosition.velocity.x, 5, cameraPosition.position.velocity.z);
        }
        if (Input.GetKeyDown("w"))
        {
            Debug.Log("W is pressed");
            cameraPosition.velocity = new Vector3(2, cameraPosition.velocity.y, cameraPosition.velocity.z);
        }
        if (Input.GetKeyDown("a"))
        {
            cameraPosition.velocity = new Vector3(cameraPosition.velocity.x, cameraPosition.velocity.y, -2);
        }
        if (Input.GetKeyDown("s"))
        {
            cameraPosition.velocity = new Vector3(-2, cameraPosition.velocity.y, cameraPosition.velocity.z);
        }
        if (Input.GetKeyDown("d"))
        {
            cameraPosition.velocity = new Vector3(cameraPosition.velocity.x, cameraPosition.velocity.y, 2);
        }*/
    }
}